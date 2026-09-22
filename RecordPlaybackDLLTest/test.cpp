#include "pch.h"
#include "../RecordPlaybackDLL/Playback/PlaybackSession.h"
#include "../RecordPlaybackDLL/Common/KeyboardEvent.h"
#include "../RecordPlaybackDLL/Common/MouseEvent.h"
#include "../RecordPlaybackDLL/Common/DeserializeEvent.h"
#include <future>
#include <limits>

using namespace std::chrono_literals;
using record_playback::PlaybackSession;

namespace {
std::unique_ptr<Event> key(WORD code, bool up = false, std::chrono::microseconds delay = 0us) {
	auto event = std::make_unique<KeyboardEvent>();
	event->virtualKeyCode = code;
	event->keyUp = up;
	event->time_since_last_event = delay;
	return event;
}
std::vector<std::unique_ptr<Event>> one(std::unique_ptr<Event> event) {
	std::vector<std::unique_ptr<Event>> events;
	events.push_back(std::move(event));
	return events;
}
PlaybackResult finish(PlaybackSession& session, uint64_t id) {
	const auto deadline = std::chrono::steady_clock::now() + 3s;
	PlaybackResult result;
	do {
		result = session.poll(id);
		if (result != PlaybackResult::Running) return result;
		std::this_thread::sleep_for(1ms);
	} while (std::chrono::steady_clock::now() < deadline);
	session.abort(id);
	return PlaybackResult::Running;
}
std::vector<unsigned char> bytes(const protobufGenerated::ProtobufInputEventList& list) {
	const auto data = list.SerializeAsString();
	return {data.begin(), data.end()};
}

// Owning C++ objects stay inside this test executable's static CRT. Exercise the
// built DLL only through its non-owning byte-buffer/scalar C ABI.
class PlaybackAbi : public ::testing::Test {
protected:
	HMODULE module_ = nullptr;
	decltype(&iac_dll_playback_start) start_ = nullptr;
	decltype(&iac_dll_playback_poll) poll_ = nullptr;
	decltype(&iac_dll_playback_abort) abort_ = nullptr;

	void SetUp() override {
		std::wstring path(32768, L'\0');
		const auto length = GetModuleFileNameW(nullptr, &path[0], static_cast<DWORD>(path.size()));
		ASSERT_GT(length, 0u);
		ASSERT_LT(length, path.size());
		path.resize(length);
		const auto separator = path.find_last_of(L"\\/");
		ASSERT_NE(std::wstring::npos, separator);
		path.resize(separator + 1);
		path += L"RecordPlaybackDLL.dll";
		module_ = LoadLibraryExW(path.c_str(), nullptr,
			LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
		ASSERT_NE(nullptr, module_) << "LoadLibraryExW error: " << GetLastError();
		start_ = reinterpret_cast<decltype(start_)>(GetProcAddress(module_, "iac_dll_playback_start"));
		poll_ = reinterpret_cast<decltype(poll_)>(GetProcAddress(module_, "iac_dll_playback_poll"));
		abort_ = reinterpret_cast<decltype(abort_)>(GetProcAddress(module_, "iac_dll_playback_abort"));
		ASSERT_NE(nullptr, start_);
		ASSERT_NE(nullptr, poll_);
		ASSERT_NE(nullptr, abort_);
	}

	void TearDown() override {
		if (module_) FreeLibrary(module_);
	}
};
}

TEST(Playback, LongDelayAbortWakesAndDoesNotInjectDelayedEvent) {
	std::atomic<int> calls{0};
	std::promise<void> reached;
	PlaybackSession session([&](const Event&) { if (++calls == 1) reached.set_value(); return true; });
	uint64_t id;
	auto events = one(key('B', true));
	events.push_back(key('A', false, 1h));
	ASSERT_EQ(PlaybackResult::Running, session.start(std::move(events), false, id));
	ASSERT_EQ(std::future_status::ready, reached.get_future().wait_for(1s));
	std::this_thread::sleep_for(20ms);
	const auto begin = std::chrono::steady_clock::now();
	EXPECT_EQ(PlaybackResult::Cancelled, session.abort(id));
	EXPECT_LT(std::chrono::steady_clock::now() - begin, 500ms);
	EXPECT_EQ(1, calls);
}

TEST(Playback, ZeroDelayLoopIsPacedAndAbortJoins) {
	std::atomic<int> calls{0};
	PlaybackSession session([&](const Event&) { ++calls; return true; });
	uint64_t id;
	ASSERT_EQ(PlaybackResult::Running, session.start(one(key('A', true)), true, id));
	std::this_thread::sleep_for(120ms);
	const auto begin = std::chrono::steady_clock::now();
	EXPECT_EQ(PlaybackResult::Cancelled, session.abort(id));
	EXPECT_LT(std::chrono::steady_clock::now() - begin, 500ms);
	const auto stopped = calls.load();
	EXPECT_GT(stopped, 0);
	EXPECT_LT(stopped, 40);
	std::this_thread::sleep_for(10ms);
	EXPECT_EQ(stopped, calls);
}

TEST(Playback, RejectsOverlapAndStaleAbortCannotCancelNewSession) {
	PlaybackSession session([](const Event&) { return true; });
	uint64_t first, second;
	ASSERT_EQ(PlaybackResult::Running, session.start(one(key('A', false, 1h)), false, first));
	EXPECT_EQ(PlaybackResult::Busy, session.start(one(key('B')), false, second));
	EXPECT_EQ(0u, second);
	EXPECT_EQ(PlaybackResult::Cancelled, session.abort(first));
	ASSERT_EQ(PlaybackResult::Running, session.start(one(key('B', false, 1h)), false, second));
	EXPECT_NE(first, second);
	EXPECT_EQ(PlaybackResult::StaleSession, session.abort(first));
	EXPECT_EQ(PlaybackResult::Running, session.poll(second));
	EXPECT_EQ(PlaybackResult::Cancelled, session.abort(second));
}

TEST(Playback, ReleasesHeldKeysAndAllButtonsOnAbortWithoutExtraModifierReleases) {
	std::vector<WORD> released_keys;
	std::vector<std::pair<DWORD, DWORD>> released_buttons;
	std::promise<void> injected;
	int calls = 0;
	PlaybackSession session([&](const Event& event) {
		if (const auto keyboard = dynamic_cast<const KeyboardEvent*>(&event)) {
			if (keyboard->keyUp) released_keys.push_back(keyboard->virtualKeyCode);
		} else if (const auto mouse = dynamic_cast<const MouseEvent*>(&event)) {
			if (mouse->ActionType & (MOUSEEVENTF_LEFTUP | MOUSEEVENTF_RIGHTUP | MOUSEEVENTF_MIDDLEUP | MOUSEEVENTF_XUP)) {
				released_buttons.emplace_back(mouse->ActionType, mouse->wheelRotation);
				EXPECT_TRUE(mouse->relative_position);
				EXPECT_EQ(0, mouse->x);
				EXPECT_EQ(0, mouse->y);
			}
		}
		if (++calls == 2) injected.set_value();
		return true;
	});
	auto events = one(key(VK_LCONTROL));
	events.push_back(std::make_unique<MouseEvent>(0, 0, MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_MIDDLEDOWN | MOUSEEVENTF_XDOWN, XBUTTON1 | XBUTTON2, false, true));
	events.push_back(key('A', false, 1h));
	uint64_t id;
	ASSERT_EQ(PlaybackResult::Running, session.start(std::move(events), false, id));
	ASSERT_EQ(std::future_status::ready, injected.get_future().wait_for(1s));
	EXPECT_EQ(PlaybackResult::Cancelled, session.abort(id));
	EXPECT_EQ(std::vector<WORD>{VK_LCONTROL}, released_keys);
	EXPECT_EQ((std::vector<std::pair<DWORD, DWORD>>{{MOUSEEVENTF_LEFTUP,0}, {MOUSEEVENTF_RIGHTUP,0}, {MOUSEEVENTF_MIDDLEUP,0}, {MOUSEEVENTF_XUP,XBUTTON1}, {MOUSEEVENTF_XUP,XBUTTON2}}), released_buttons);
}

TEST(Playback, PartialCompoundFailureConservativelyReleasesAttemptedDowns) {
	std::vector<DWORD> flags;
	PlaybackSession session([&](const Event& event) {
		const auto& mouse = dynamic_cast<const MouseEvent&>(event);
		flags.push_back(mouse.ActionType);
		return flags.size() != 1; // Windows inserted one down from a compound event.
	});
	uint64_t id;
	const DWORD downs = MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_RIGHTDOWN;
	ASSERT_EQ(PlaybackResult::Running, session.start(one(std::make_unique<MouseEvent>(0, 0, downs, 0, false, true)), false, id));
	EXPECT_EQ(PlaybackResult::InjectionFailed, finish(session, id));
	EXPECT_EQ((std::vector<DWORD>{downs, MOUSEEVENTF_LEFTUP, MOUSEEVENTF_RIGHTUP}), flags);
}

TEST(Playback, FailedReleaseDoesNotForgetHeldKeyAndCleanupContinues) {
	std::vector<WORD> calls;
	PlaybackSession session([&](const Event& event) {
		const auto& keyboard = dynamic_cast<const KeyboardEvent&>(event);
		calls.push_back(keyboard.virtualKeyCode);
		return calls.size() != 3;
	});
	auto events = one(key('A'));
	events.push_back(key('B'));
	events.push_back(key('A', true));
	uint64_t id;
	ASSERT_EQ(PlaybackResult::Running, session.start(std::move(events), false, id));
	EXPECT_EQ(PlaybackResult::InjectionFailed, finish(session, id));
	EXPECT_EQ((std::vector<WORD>{'A','B','A','A','B'}), calls);
}

TEST(Playback, BalancedOneShotKeepsOrderAndDoesNotAddReleases) {
	std::vector<bool> ups;
	PlaybackSession session([&](const Event& event) { ups.push_back(dynamic_cast<const KeyboardEvent&>(event).keyUp); return true; });
	auto events = one(key('A'));
	events.push_back(key('A', true));
	uint64_t id;
	ASSERT_EQ(PlaybackResult::Running, session.start(std::move(events), false, id));
	EXPECT_EQ(PlaybackResult::Finished, finish(session, id));
	EXPECT_EQ((std::vector<bool>{false, true}), ups);
}

TEST(Playback, PollRemainsRunningAndRestartIsBlockedUntilHeldInputCleanupReturns) {
	for (const bool released : {false, true}) {
		SCOPED_TRACE(released);
		std::promise<void> cleanup_started, allow_cleanup;
		auto allowed = allow_cleanup.get_future().share();
		PlaybackSession session([&](const Event& event) {
			if (!dynamic_cast<const KeyboardEvent&>(event).keyUp) return true;
			cleanup_started.set_value();
			allowed.wait();
			return released;
		});
		uint64_t id;
		ASSERT_EQ(PlaybackResult::Running, session.start(one(key('A')), false, id));
		const auto reached = cleanup_started.get_future().wait_for(1s);
		if (reached != std::future_status::ready) {
			allow_cleanup.set_value();
			FAIL() << "Session did not attempt held-key cleanup";
		}
		auto ownership = std::async(std::launch::async, [&] {
			EXPECT_EQ(PlaybackResult::Running, session.poll(id));
			uint64_t next;
			EXPECT_EQ(PlaybackResult::Busy, session.start(one(key('B')), false, next));
			EXPECT_EQ(0u, next);
		});
		const auto checked = ownership.wait_for(1s);
		allow_cleanup.set_value();
		EXPECT_EQ(std::future_status::ready, checked);
		ownership.get();
		EXPECT_EQ(released ? PlaybackResult::Finished : PlaybackResult::InjectionFailed, finish(session, id));
	}
}

TEST(Playback, DestructionCancelsAndReleasesWithoutDetachedWork) {
	std::atomic<int> calls{0};
	std::promise<void> injected;
	{
		PlaybackSession session([&](const Event&) { if (++calls == 1) injected.set_value(); return true; });
		auto events = one(key('A'));
		events.push_back(key('B', false, 1h));
		uint64_t id;
		ASSERT_EQ(PlaybackResult::Running, session.start(std::move(events), false, id));
		ASSERT_EQ(std::future_status::ready, injected.get_future().wait_for(1s));
	}
	EXPECT_EQ(2, calls);
}

TEST(Playback, UsesAccumulatedDeadlinesInsteadOfAddingInjectionDuration) {
	std::vector<std::chrono::steady_clock::time_point> times;
	PlaybackSession session([&](const Event&) { times.push_back(std::chrono::steady_clock::now()); std::this_thread::sleep_for(20ms); return true; });
	std::vector<std::unique_ptr<Event>> events;
	for (int i = 0; i < 8; ++i) events.push_back(key('A', true, 40ms));
	uint64_t id;
	ASSERT_EQ(PlaybackResult::Running, session.start(std::move(events), false, id));
	EXPECT_EQ(PlaybackResult::Finished, finish(session, id));
	ASSERT_EQ(8u, times.size());
	EXPECT_GE(times.back() - times.front(), 270ms);
	EXPECT_LT(times.back() - times.front(), 400ms);
}

TEST(Playback, RejectsEmptyNullNegativeAndCumulativeOverflowBeforeInjection) {
	int calls = 0;
	PlaybackSession session([&](const Event&) { ++calls; return true; });
	uint64_t id;
	EXPECT_EQ(PlaybackResult::InvalidInput, session.start({}, true, id));
	EXPECT_EQ(PlaybackResult::InvalidInput, session.start(one(nullptr), false, id));
	EXPECT_EQ(PlaybackResult::InvalidInput, session.start(one(key('A', false, -1us)), false, id));
	EXPECT_EQ(PlaybackResult::InvalidInput, session.start(one(key('A', false, (std::chrono::microseconds::max)())), false, id));
	const auto half = std::chrono::duration_cast<std::chrono::microseconds>((std::chrono::steady_clock::duration::max)()) / 2;
	auto events = one(key('A', false, half));
	events.push_back(key('B', false, half));
	EXPECT_EQ(PlaybackResult::InvalidInput, session.start(std::move(events), false, id));
	EXPECT_EQ(0, calls);
}

TEST_F(PlaybackAbi, RejectsMalformedMissingPayloadEmptyAndOversizedBuffers) {
	uint64_t id = 9;
	const unsigned char malformed[] = {0x0a, 0xff};
	EXPECT_EQ(PlaybackResult::InvalidInput, start_(malformed, sizeof malformed, 0, &id));
	EXPECT_EQ(0u, id);
	EXPECT_EQ(PlaybackResult::InvalidInput, start_(nullptr, 0, 1, &id));
	EXPECT_EQ(PlaybackResult::InvalidInput, start_(malformed, (std::numeric_limits<size_t>::max)(), 0, &id));
	EXPECT_EQ(PlaybackResult::InvalidInput, start_(malformed, sizeof malformed, 0, nullptr));
	protobufGenerated::ProtobufInputEventList list;
	list.add_inputevents();
	const auto data = bytes(list);
	EXPECT_EQ(PlaybackResult::InvalidInput, start_(data.data(), data.size(), 0, &id));
	EXPECT_EQ(PlaybackResult::StaleSession, poll_(0));
	EXPECT_EQ(PlaybackResult::StaleSession, abort_(0));
}

TEST_F(PlaybackAbi, RejectsUint64AndChronoOverflowWithoutInjecting) {
	for (uint64_t delay : { (std::numeric_limits<uint64_t>::max)(), static_cast<uint64_t>((std::numeric_limits<int64_t>::max)()) }) {
		protobufGenerated::ProtobufInputEventList list;
		auto* event = list.add_inputevents();
		event->mutable_keyboardevent()->set_virtualkeycode('A');
		event->set_timesincelastevent(delay);
		const auto data = bytes(list);
		uint64_t id;
		EXPECT_EQ(PlaybackResult::InvalidInput, start_(data.data(), data.size(), 0, &id));
		EXPECT_EQ(0u, id);
	}
}

TEST(Playback, StartupModifiersAreReleasedOnceOutsideLoop) {
	std::atomic<int> modifier_releases{0};
	std::atomic<int> passes{0};
	PlaybackSession session([&](const Event& event) {
		const auto& keyboard = dynamic_cast<const KeyboardEvent&>(event);
		if (keyboard.virtualKeyCode == 'A') ++passes;
		else { EXPECT_TRUE(keyboard.keyUp); ++modifier_releases; }
		return true;
	}, true);
	uint64_t id;
	ASSERT_EQ(PlaybackResult::Running, session.start(one(key('A', true)), true, id));
	std::this_thread::sleep_for(60ms);
	EXPECT_EQ(PlaybackResult::Cancelled, session.abort(id));
	EXPECT_GT(passes, 1);
	EXPECT_EQ(6, modifier_releases);
}

TEST(Playback, DisablingLoopFinishesCurrentPassAndDoesNotRestart) {
	std::promise<void> injected;
	std::atomic<int> calls{0};
	PlaybackSession session([&](const Event&) { if (++calls == 1) injected.set_value(); return true; });
	auto events = one(key('A', true));
	events.push_back(key('B', true, 80ms));
	uint64_t id;
	ASSERT_EQ(PlaybackResult::Running, session.start(std::move(events), true, id));
	ASSERT_EQ(std::future_status::ready, injected.get_future().wait_for(1s));
	EXPECT_EQ(PlaybackResult::Running, session.set_loop(id, false));
	EXPECT_EQ(PlaybackResult::Finished, finish(session, id));
	EXPECT_EQ(2, calls);
	EXPECT_EQ(PlaybackResult::Finished, session.set_loop(id, true));
	EXPECT_EQ(PlaybackResult::Finished, session.poll(id));
	EXPECT_EQ(2, calls);
}

TEST(Playback, LargeZeroDelayOneShotYieldsAndCanBeCancelled) {
	std::atomic<int> calls{0};
	std::promise<void> reached;
	PlaybackSession session([&](const Event&) { if (++calls == 64) reached.set_value(); return true; });
	std::vector<std::unique_ptr<Event>> events;
	for (int i = 0; i < 100000; ++i) events.push_back(key('A', true));
	uint64_t id;
	ASSERT_EQ(PlaybackResult::Running, session.start(std::move(events), false, id));
	ASSERT_EQ(std::future_status::ready, reached.get_future().wait_for(1s));
	const auto begin = std::chrono::steady_clock::now();
	EXPECT_EQ(PlaybackResult::Cancelled, session.abort(id));
	EXPECT_LT(std::chrono::steady_clock::now() - begin, 500ms);
	EXPECT_LT(calls, 100000);
}

TEST(Playback, ThrowingSinkStillAttemptsEveryHeldRelease) {
	std::vector<WORD> calls;
	PlaybackSession session([&](const Event& event) {
		const auto& keyboard = dynamic_cast<const KeyboardEvent&>(event);
		calls.push_back(keyboard.virtualKeyCode);
		if (calls.size() >= 2) throw std::runtime_error("fake failure");
		return true;
	});
	auto events = one(key('A'));
	events.push_back(key('B'));
	uint64_t id;
	ASSERT_EQ(PlaybackResult::Running, session.start(std::move(events), false, id));
	EXPECT_EQ(PlaybackResult::InjectionFailed, finish(session, id));
	EXPECT_EQ((std::vector<WORD>{'A', 'B', 'A', 'B'}), calls);
}
