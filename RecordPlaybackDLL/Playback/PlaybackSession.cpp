#include "PlaybackSession.h"
#include "../Common/KeyboardEvent.h"
#include "../Common/MouseEvent.h"
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
	for (const auto& event : events) {
		if (!event || event->time_since_last_event < 0us || event->time_since_last_event > remaining)
			return PlaybackResult::InvalidInput;
		remaining -= event->time_since_last_event;
	}
	if (worker_.joinable()) worker_.join();
	cancelled_ = false;
	loop_ = loop;
	result_ = PlaybackResult::Running;
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
		if (release_start_modifiers_) {
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
			for (const auto& event : events) {
				if (cancelled_) break;
				const auto remaining = std::chrono::duration_cast<std::chrono::microseconds>((Clock::time_point::max)() - deadline);
				if (event->time_since_last_event > remaining) throw std::overflow_error("Playback deadline overflow");
				deadline += event->time_since_last_event;
				if (deadline > Clock::now()) batch_count = 0;
				if (!wait_until(deadline) || cancelled_) break;
				// A compound mouse SendInput can insert only a prefix. Account for
				// attempted downs conservatively, then confirm ups only on success.
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
