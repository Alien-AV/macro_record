#include "PlaybackSession.h"
#include "../Common/KeyboardEvent.h"
#include "../Common/MouseEvent.h"
#include "../Common/WaitEvent.h"
#include "../Common/DelayEvent.h"
#include <array>
#include <algorithm>
#include <stdexcept>

namespace record_playback {
using Clock = std::chrono::steady_clock;
using namespace std::chrono_literals;

PlaybackSession::PlaybackSession(Sink sink, bool release_start_modifiers)
	: sink_(std::move(sink)), release_start_modifiers_(release_start_modifiers) {}
PlaybackSession::~PlaybackSession() { shutdown(); }

PlaybackResult PlaybackSession::start(std::vector<std::unique_ptr<Event>> events, bool loop, uint64_t& id) {
	std::lock_guard<std::mutex> lock(lifecycle_mutex_);
	id = 0;
	if (result_ == PlaybackResult::Running) return PlaybackResult::Busy;
	if (events.empty()) return PlaybackResult::InvalidInput;
	// Validate the whole schedule before injection, including chrono's finer
	// internal duration and cumulative time_point arithmetic.
	auto remaining = std::chrono::duration_cast<std::chrono::microseconds>((Clock::time_point::max)() - Clock::now());
	std::array<bool, 256> keys{};
	std::array<bool, 5> buttons{};
	bool has_wait = false;
	for (const auto& event : events) {
		if (!event || event->time_since_last_event < 0us || event->time_since_last_event > remaining)
			return PlaybackResult::InvalidInput;
		remaining -= event->time_since_last_event;
		if (const auto wait = dynamic_cast<const WaitEvent*>(event.get())) {
			has_wait = true;
			if (!WaitEvent::valid(wait->condition) || std::any_of(keys.begin(), keys.end(), [](bool v) { return v; })
				|| std::any_of(buttons.begin(), buttons.end(), [](bool v) { return v; })) return PlaybackResult::InvalidInput;
			const auto timeout = std::chrono::microseconds(wait->condition.timeout_us());
			if (timeout > remaining) return PlaybackResult::InvalidInput;
			remaining -= timeout;
		} else if (const auto delay = dynamic_cast<const DelayEvent*>(event.get())) {
			if (!DelayEvent::valid(delay->duration.count()) || delay->duration > remaining) return PlaybackResult::InvalidInput;
			remaining -= delay->duration;
		} else if (const auto key = dynamic_cast<const KeyboardEvent*>(event.get())) {
			if (!key->virtualKeyCode || key->virtualKeyCode >= keys.size()) return PlaybackResult::InvalidInput;
			keys[key->virtualKeyCode] = !key->keyUp;
		} else if (const auto mouse = dynamic_cast<const MouseEvent*>(event.get())) {
			constexpr DWORD downs[] = { 2, 8, 32, 128, 128 }, ups[] = { 4, 16, 64, 256, 256 };
			const auto x = mouse->wheelRotation == 0 ? 1 : mouse->wheelRotation;
			for (size_t i = 0; i < buttons.size(); ++i) {
				if (i >= 3 && !(x & (1 << (i - 3)))) continue;
				if (mouse->ActionType & downs[i]) buttons[i] = true;
				if (mouse->ActionType & ups[i]) buttons[i] = false;
			}
		} else return PlaybackResult::InvalidInput;
	}
	const bool loop_safe = !has_wait || (!std::any_of(keys.begin(), keys.end(), [](bool v) { return v; })
		&& !std::any_of(buttons.begin(), buttons.end(), [](bool v) { return v; }));
	if (loop && !loop_safe) return PlaybackResult::InvalidInput;
	if (worker_.joinable()) worker_.join();
	cancelled_ = false;
	loop_ = loop;
	loop_safe_ = loop_safe;
	result_ = PlaybackResult::Running;
	waiting_ = {};
	failed_wait_ = {};
	try {
		worker_ = std::thread(&PlaybackSession::run, this, std::move(events));
		id = ++generation_;
		return PlaybackResult::Running;
	} catch (...) {
		result_ = PlaybackResult::InternalError;
		return PlaybackResult::InternalError;
	}
}

PlaybackResult PlaybackSession::poll(uint64_t id) {
	std::lock_guard<std::mutex> lock(lifecycle_mutex_);
	if (!id || id != generation_) return PlaybackResult::StaleSession;
	const auto result = result_.load();
	if (result != PlaybackResult::Running && worker_.joinable()) worker_.join();
	return result;
}

PlaybackResult PlaybackSession::wait_request(uint64_t id, PlaybackWaitRequest& request) {
	std::lock_guard<std::mutex> lifecycle(lifecycle_mutex_);
	request = {};
	if (!id || id != generation_) return PlaybackResult::StaleSession;
	std::lock_guard<std::mutex> lock(wait_mutex_);
	const auto now = Clock::now();
	const auto result = result_.load();
	if (result == PlaybackResult::WaitTimedOut || result == PlaybackResult::WaitFailed) request = failed_wait_;
	else if (result == PlaybackResult::Running && waiting_.occurrence && !cancelled_ && resolution_ == 0 && now < wait_deadline_) {
		request = waiting_;
		request.remaining_us = static_cast<uint64_t>(std::chrono::duration_cast<std::chrono::microseconds>(wait_deadline_ - now).count());
	}
	return result;
}

PlaybackResult PlaybackSession::resolve_wait(uint64_t id, uint64_t occurrence, bool satisfied) {
	std::lock_guard<std::mutex> lifecycle(lifecycle_mutex_);
	if (!id || id != generation_) return PlaybackResult::StaleSession;
	std::lock_guard<std::mutex> lock(wait_mutex_);
	if (!occurrence || occurrence != waiting_.occurrence || resolution_ != 0 || cancelled_ || Clock::now() >= wait_deadline_)
		return PlaybackResult::StaleWait;
	resolution_ = satisfied ? 1 : -1;
	wake_.notify_all();
	return PlaybackResult::Running;
}

void PlaybackSession::stop_and_join() {
	{
		std::lock_guard<std::mutex> lock(wait_mutex_);
		cancelled_ = true;
	}
	wake_.notify_all();
	if (worker_.joinable()) worker_.join();
}

PlaybackResult PlaybackSession::set_loop(uint64_t id, bool loop) {
	std::lock_guard<std::mutex> lock(lifecycle_mutex_);
	if (!id || id != generation_) return PlaybackResult::StaleSession;
	if (loop && !loop_safe_) return PlaybackResult::InvalidInput;
	loop_ = loop;
	return result_;
}

PlaybackResult PlaybackSession::abort(uint64_t id) {
	std::lock_guard<std::mutex> lock(lifecycle_mutex_);
	if (!id || id != generation_) return PlaybackResult::StaleSession;
	stop_and_join();
	return result_;
}

void PlaybackSession::shutdown() {
	std::lock_guard<std::mutex> lock(lifecycle_mutex_);
	stop_and_join();
}

bool PlaybackSession::wait_until(Clock::time_point deadline) {
	std::unique_lock<std::mutex> lock(wait_mutex_);
	return !wake_.wait_until(lock, deadline, [this] { return cancelled_.load(); });
}

void PlaybackSession::run(std::vector<std::unique_ptr<Event>> events) noexcept {
	std::array<bool, 256> keys{};
	std::array<bool, 5> buttons{};
	constexpr DWORD downs[] = { MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_XDOWN, MOUSEEVENTF_XDOWN };
	constexpr DWORD ups[] = { MOUSEEVENTF_LEFTUP, MOUSEEVENTF_RIGHTUP, MOUSEEVENTF_MIDDLEUP, MOUSEEVENTF_XUP, MOUSEEVENTF_XUP };
	auto outcome = PlaybackResult::Finished;
	try {
		// Preserve Ctrl+E startup behavior exactly once, outside the repeated list.
		if (release_start_modifiers_ && std::any_of(events.begin(), events.end(), [](const auto& event) {
			return dynamic_cast<const KeyboardEvent*>(event.get()) || dynamic_cast<const MouseEvent*>(event.get()); })) {
			for (WORD key : {VK_LSHIFT, VK_LCONTROL, VK_LMENU, VK_RSHIFT, VK_RCONTROL, VK_RMENU}) {
				if (cancelled_) break;
				KeyboardEvent release;
				release.virtualKeyCode = key;
				release.keyUp = true;
				if (!sink_(release)) { outcome = PlaybackResult::InjectionFailed; break; }
			}
		}
		auto deadline = Clock::now();
		size_t batch_count = 0;
		do {
			if (outcome != PlaybackResult::Finished) break;
			const auto pass_start = deadline;
			for (size_t index = 0; index < events.size(); ++index) {
				const auto& event = events[index];
				if (cancelled_) break;
				const auto remaining = std::chrono::duration_cast<std::chrono::microseconds>((Clock::time_point::max)() - deadline);
				if (event->time_since_last_event > remaining) throw std::overflow_error("Playback deadline overflow");
				deadline += event->time_since_last_event;
				if (const auto delay = dynamic_cast<const DelayEvent*>(event.get())) {
					if (delay->duration > std::chrono::duration_cast<std::chrono::microseconds>((Clock::time_point::max)() - deadline))
						throw std::overflow_error("Fixed delay deadline overflow");
					deadline += delay->duration;
				}
				if (deadline > Clock::now()) batch_count = 0;
				if (!wait_until(deadline) || cancelled_) break;
				if (const auto wait = dynamic_cast<const WaitEvent*>(event.get())) {
					std::unique_lock<std::mutex> lock(wait_mutex_);
					waiting_ = { ++occurrence_, static_cast<uint64_t>(index), wait->condition.timeout_us() };
					wait_deadline_ = Clock::now() + std::chrono::microseconds(wait->condition.timeout_us());
					resolution_ = 0;
					wake_.wait_until(lock, wait_deadline_, [this] { return cancelled_.load() || resolution_ != 0; });
					if (!cancelled_ && resolution_ != 1) failed_wait_ = { waiting_.occurrence, waiting_.event_index, 0 };
					waiting_ = {};
					if (cancelled_) break;
					if (resolution_ != 1) { outcome = resolution_ == -1 ? PlaybackResult::WaitFailed : PlaybackResult::WaitTimedOut; break; }
					deadline = Clock::now();
					batch_count = 0;
					continue;
				}
				// A compound mouse SendInput can insert only a prefix. Account for
				// attempted downs conservatively, then confirm ups only on success.
				if (!dynamic_cast<const DelayEvent*>(event.get())) {
				if (const auto keyboard = dynamic_cast<const KeyboardEvent*>(event.get())) {
					if (keyboard->virtualKeyCode < keys.size() && !keyboard->keyUp) keys[keyboard->virtualKeyCode] = true;
				} else if (const auto mouse = dynamic_cast<const MouseEvent*>(event.get())) {
					const auto xbuttons = mouse->wheelRotation == 0 ? XBUTTON1 : mouse->wheelRotation;
					for (size_t i = 0; i < buttons.size(); ++i) {
						if (i >= 3 && !(xbuttons & (i == 3 ? XBUTTON1 : XBUTTON2))) continue;
						if (mouse->ActionType & downs[i]) buttons[i] = true;
					}
				}
				if (!sink_(*event)) { outcome = PlaybackResult::InjectionFailed; break; }
				if (const auto keyboard = dynamic_cast<const KeyboardEvent*>(event.get())) {
					if (keyboard->virtualKeyCode < keys.size() && keyboard->keyUp) keys[keyboard->virtualKeyCode] = false;
				} else if (const auto mouse = dynamic_cast<const MouseEvent*>(event.get())) {
					const auto xbuttons = mouse->wheelRotation == 0 ? XBUTTON1 : mouse->wheelRotation;
					for (size_t i = 0; i < buttons.size(); ++i) {
						if (i >= 3 && !(xbuttons & (i == 3 ? XBUTTON1 : XBUTTON2))) continue;
						if (mouse->ActionType & ups[i]) buttons[i] = false;
					}
				}
				}
				// Bound overdue-event bursts as well as zero-delay lists, so Windows
				// input processing/hotkeys can run even while catching up.
				if (++batch_count >= 64) {
					if (!wait_until(Clock::now() + 1ms)) break;
					batch_count = 0;
				}
			}
			if (cancelled_ || outcome != PlaybackResult::Finished || !loop_) break;
			// Short loops run at most 200 passes/second. Longer macros retain the
			// accumulated monotonic schedule instead of adding per-event drift.
			if (deadline - pass_start < 5ms) {
				deadline = (std::max)(Clock::now(), pass_start + 5ms);
				if (!wait_until(deadline)) break;
			}
		} while (!cancelled_ && loop_);
		if (cancelled_ && outcome == PlaybackResult::Finished) outcome = PlaybackResult::Cancelled;
	} catch (...) { outcome = PlaybackResult::InternalError; }

	// Release input held or potentially inserted by this session, also on completion of
	// an unbalanced macro. Physical-user modifier interference is a separate issue.
	for (size_t key = 0; key < keys.size(); ++key) {
		if (!keys[key]) continue;
		KeyboardEvent release;
		release.virtualKeyCode = static_cast<WORD>(key);
		release.keyUp = true;
		try { if (!sink_(release)) outcome = PlaybackResult::InjectionFailed; }
		catch (...) { outcome = PlaybackResult::InjectionFailed; }
	}
	for (size_t i = 0; i < buttons.size(); ++i) {
		if (!buttons[i]) continue;
		MouseEvent release(0, 0, ups[i], i < 3 ? 0 : (i == 3 ? XBUTTON1 : XBUTTON2), false, true);
		try { if (!sink_(release)) outcome = PlaybackResult::InjectionFailed; }
		catch (...) { outcome = PlaybackResult::InjectionFailed; }
	}
	result_ = outcome;
}
}
