#include "pch.h"
#include "../RecordPlaybackDLL/Playback/PlaybackSession.h"
#include "../RecordPlaybackDLL/Common/WaitEvent.h"
#include "../RecordPlaybackDLL/Common/KeyboardEvent.h"
#include "../RecordPlaybackDLL/Common/MouseEvent.h"
#include "../RecordPlaybackDLL/Common/DeserializeEvent.h"
#include <future>

using namespace std::chrono_literals;
using record_playback::PlaybackSession;
namespace {
std::unique_ptr<WaitEvent> wait_event(std::chrono::microseconds timeout = 1s) {
    auto event = std::make_unique<WaitEvent>();
    event->condition.set_semantics_version(1);
    event->condition.set_timeout_us(timeout.count());
    event->condition.set_poll_interval_us(10000);
    event->condition.mutable_window()->mutable_target()->set_title("Fake target");
    return event;
}
std::unique_ptr<KeyboardEvent> key_event(bool up, std::chrono::microseconds delay = 0us) {
    auto event = std::make_unique<KeyboardEvent>(); event->virtualKeyCode = 'A'; event->keyUp = up; event->time_since_last_event = delay; return event;
}
std::vector<std::unique_ptr<Event>> only_wait(std::chrono::microseconds timeout = 1s) {
    std::vector<std::unique_ptr<Event>> events; events.push_back(wait_event(timeout)); return events;
}
PlaybackWaitRequest request(PlaybackSession& session, uint64_t id, uint64_t previous = 0) {
    PlaybackWaitRequest result{};
    const auto end = std::chrono::steady_clock::now() + 2s;
    while (std::chrono::steady_clock::now() < end) {
        if (session.wait_request(id, result) != PlaybackResult::Running) break;
        if (result.occurrence && result.occurrence != previous) return result;
        std::this_thread::sleep_for(1ms);
    }
    return {};
}
PlaybackResult finished(PlaybackSession& session, uint64_t id) {
    const auto end = std::chrono::steady_clock::now() + 2s;
    while (std::chrono::steady_clock::now() < end) {
        auto result = session.poll(id); if (result != PlaybackResult::Running) return result;
        std::this_thread::sleep_for(1ms);
    }
    session.abort(id); return PlaybackResult::Running;
}
}

TEST(ConditionalWait, WaitOnlyHasNoStartupReleasesOrSinkCalls) {
    std::atomic<int> calls{0}; PlaybackSession session([&](const Event&) { ++calls; return true; }, true);
    uint64_t id; ASSERT_EQ(PlaybackResult::Running, session.start(only_wait(), false, id));
    const auto active = request(session, id); ASSERT_NE(0u, active.occurrence); EXPECT_EQ(0u, active.event_index);
    EXPECT_GT(active.remaining_us, 0u);
    EXPECT_EQ(PlaybackResult::Running, session.resolve_wait(id, active.occurrence, true));
    EXPECT_EQ(PlaybackResult::Finished, finished(session, id)); EXPECT_EQ(0, calls);
}

TEST(ConditionalWait, NativeDeadlineStopsWithoutObserverAndRejectsLateResponse) {
    std::atomic<int> calls{0}; PlaybackSession session([&](const Event&) { ++calls; return true; });
    auto events = only_wait(50ms); events.push_back(key_event(true));
    uint64_t id; ASSERT_EQ(PlaybackResult::Running, session.start(std::move(events), false, id));
    auto active = request(session, id); ASSERT_NE(0u, active.occurrence);
    EXPECT_EQ(PlaybackResult::WaitTimedOut, finished(session, id));
    EXPECT_EQ(PlaybackResult::StaleWait, session.resolve_wait(id, active.occurrence, true)); EXPECT_EQ(0, calls);
}

TEST(ConditionalWait, CancellationWakesNativeWaitAndRetainsSessionOwnership) {
    PlaybackSession session([](const Event&) { ADD_FAILURE() << "Wait injected input"; return true; });
    uint64_t id; ASSERT_EQ(PlaybackResult::Running, session.start(only_wait(1h), false, id));
    auto active = request(session, id); ASSERT_NE(0u, active.occurrence);
    uint64_t other; EXPECT_EQ(PlaybackResult::Busy, session.start(only_wait(), false, other));
    auto started = std::chrono::steady_clock::now(); EXPECT_EQ(PlaybackResult::Cancelled, session.abort(id));
    EXPECT_LT(std::chrono::steady_clock::now() - started, 500ms);
    EXPECT_EQ(PlaybackResult::StaleWait, session.resolve_wait(id, active.occurrence, true));
    ASSERT_EQ(PlaybackResult::Running, session.start(only_wait(), false, other));
    EXPECT_EQ(PlaybackResult::StaleSession, session.resolve_wait(id, active.occurrence, true));
    session.abort(other);
}

TEST(ConditionalWait, OccurrencesChangeAcrossConsecutiveWaitsAndRepeats) {
    PlaybackSession session([](const Event&) { return true; }); auto events = only_wait(); events.push_back(wait_event());
    uint64_t id; ASSERT_EQ(PlaybackResult::Running, session.start(std::move(events), true, id));
    auto first = request(session, id); ASSERT_NE(0u, first.occurrence);
    EXPECT_EQ(PlaybackResult::Running, session.resolve_wait(id, first.occurrence, true));
    EXPECT_EQ(PlaybackResult::StaleWait, session.resolve_wait(id, first.occurrence, true));
    auto second = request(session, id, first.occurrence); ASSERT_NE(0u, second.occurrence); EXPECT_EQ(1u, second.event_index);
    EXPECT_EQ(PlaybackResult::Running, session.resolve_wait(id, second.occurrence, true));
    auto repeated = request(session, id, second.occurrence); ASSERT_NE(0u, repeated.occurrence); EXPECT_EQ(0u, repeated.event_index);
    EXPECT_NE(first.occurrence, repeated.occurrence);
    EXPECT_EQ(PlaybackResult::StaleWait, session.resolve_wait(id, first.occurrence, true)); session.abort(id);
}

TEST(ConditionalWait, SuccessfulWaitRebasesSubsequentInputDelay) {
    std::promise<std::chrono::steady_clock::time_point> inserted;
    PlaybackSession session([&](const Event&) { inserted.set_value(std::chrono::steady_clock::now()); return true; });
    auto events = only_wait(); events.push_back(key_event(true, 100ms));
    uint64_t id; ASSERT_EQ(PlaybackResult::Running, session.start(std::move(events), false, id));
    auto active = request(session, id); ASSERT_NE(0u, active.occurrence);
    std::this_thread::sleep_for(150ms); auto resumed = std::chrono::steady_clock::now();
    EXPECT_EQ(PlaybackResult::Running, session.resolve_wait(id, active.occurrence, true));
    auto future = inserted.get_future(); ASSERT_EQ(std::future_status::ready, future.wait_for(2s));
    EXPECT_GE(future.get() - resumed, 90ms); EXPECT_EQ(PlaybackResult::Finished, finished(session, id));
}

TEST(ConditionalWait, InvalidLaterWaitOrHeldInputFailsBeforeAnyStartupOrInput) {
    std::atomic<int> calls{0}; PlaybackSession session([&](const Event&) { ++calls; return true; }, true); uint64_t id;
    auto events = only_wait(); static_cast<WaitEvent*>(events[0].get())->condition.set_semantics_version(2);
    events.insert(events.begin(), key_event(true));
    EXPECT_EQ(PlaybackResult::InvalidInput, session.start(std::move(events), false, id));
    events = only_wait(); events.insert(events.begin(), key_event(false));
    EXPECT_EQ(PlaybackResult::InvalidInput, session.start(std::move(events), false, id));
    events = only_wait(); events.insert(events.begin(), std::make_unique<MouseEvent>(0, 0, MOUSEEVENTF_LEFTDOWN, 0, false, true));
    EXPECT_EQ(PlaybackResult::InvalidInput, session.start(std::move(events), false, id));
    events = only_wait(); events.push_back(key_event(false));
    EXPECT_EQ(PlaybackResult::InvalidInput, session.start(std::move(events), true, id)); EXPECT_EQ(0, calls);
}

TEST(ConditionalWait, ExplicitFailureStopsBeforeLaterInputAndPreDelayPrecedesObservation) {
    std::atomic<int> calls{0}; PlaybackSession session([&](const Event&) { ++calls; return true; });
    auto events = only_wait(); events[0]->time_since_last_event = 100ms; events.push_back(key_event(true));
    uint64_t id; ASSERT_EQ(PlaybackResult::Running, session.start(std::move(events), false, id));
    PlaybackWaitRequest early{}; EXPECT_EQ(PlaybackResult::Running, session.wait_request(id, early)); EXPECT_EQ(0u, early.occurrence);
    auto active = request(session, id); ASSERT_NE(0u, active.occurrence);
    EXPECT_EQ(PlaybackResult::Running, session.resolve_wait(id, active.occurrence, false));
    EXPECT_EQ(PlaybackResult::WaitFailed, finished(session, id)); EXPECT_EQ(0, calls);
}

TEST(ConditionalWait, CodecRejectsUnsupportedSemanticsAndMissingProviders) {
    auto event = wait_event(); auto bytes = event->serialize(); auto decoded = record_playback::deserialize_event(*bytes);
    ASSERT_NE(nullptr, dynamic_cast<WaitEvent*>(decoded.get()));
    event->condition.set_trigger(static_cast<protobufGenerated::WaitTrigger>(99));
    EXPECT_THROW(record_playback::deserialize_event(*event->serialize()), std::invalid_argument);
    event->condition.set_trigger(protobufGenerated::IS_TRUE); event->condition.clear_condition();
    EXPECT_THROW(record_playback::deserialize_event(*event->serialize()), std::invalid_argument);
}
