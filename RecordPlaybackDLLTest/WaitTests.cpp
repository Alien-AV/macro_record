#include "pch.h"
#include "../RecordPlaybackDLL/Playback/PlaybackSession.h"
#include "../RecordPlaybackDLL/Common/WaitEvent.h"
#include "../RecordPlaybackDLL/Common/KeyboardEvent.h"
#include "../RecordPlaybackDLL/Common/MouseEvent.h"
#include "../RecordPlaybackDLL/Common/DeserializeEvent.h"
#include "../RecordPlaybackDLL/Common/DelayEvent.h"
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

TEST(FixedDelay, StandalonePreDelayAndDurationNeverInject) {
    PlaybackSession session([](const Event&) { ADD_FAILURE() << "Delay injected input"; return true; }, true);
    auto pause = std::make_unique<DelayEvent>(); pause->duration = 35ms; pause->time_since_last_event = 25ms;
    std::vector<std::unique_ptr<Event>> events; events.push_back(std::move(pause));
    uint64_t id; const auto start = std::chrono::steady_clock::now();
    ASSERT_EQ(PlaybackResult::Running, session.start(std::move(events), false, id));
    EXPECT_EQ(PlaybackResult::Finished, finished(session, id));
    EXPECT_GE(std::chrono::steady_clock::now() - start, 60ms);
}

TEST(FixedDelay, HeldKeyIsPreservedUntilCancellationCleanup) {
    std::promise<void> pressed; std::atomic<int> releases{0};
    PlaybackSession session([&](const Event& event) {
        const auto key = dynamic_cast<const KeyboardEvent*>(&event);
        if (!key) { ADD_FAILURE() << "Delay reached sink"; return false; }
        if (key->keyUp) ++releases; else pressed.set_value(); return true;
    });
    auto pause = std::make_unique<DelayEvent>(); pause->duration = 1h;
    std::vector<std::unique_ptr<Event>> events; events.push_back(key_event(false)); events.push_back(std::move(pause)); events.push_back(key_event(true));
    uint64_t id; ASSERT_EQ(PlaybackResult::Running, session.start(std::move(events), false, id));
    ASSERT_EQ(std::future_status::ready, pressed.get_future().wait_for(1s));
    EXPECT_EQ(0, releases.load());
    const auto start = std::chrono::steady_clock::now();
    EXPECT_EQ(PlaybackResult::Cancelled, session.abort(id));
    EXPECT_LT(std::chrono::steady_clock::now() - start, 500ms); EXPECT_EQ(1, releases.load());
}

TEST(FixedDelay, WireRoundTripsAndInvalidDurationRejects) {
    DelayEvent source; source.duration = 123456us; source.time_since_last_event = 42us;
    const auto decoded = record_playback::deserialize_event(*source.serialize());
    const auto delay = dynamic_cast<const DelayEvent*>(decoded.get()); ASSERT_NE(nullptr, delay);
    EXPECT_EQ(123456us, delay->duration); EXPECT_EQ(42us, delay->time_since_last_event);
    source.duration = 0us; EXPECT_NO_THROW(record_playback::deserialize_event(*source.serialize()));
    source.duration = 25h; EXPECT_THROW(record_playback::deserialize_event(*source.serialize()), std::invalid_argument);
}

TEST(FixedDelay, ZeroDurationBatchStillYieldsAndCanBeCancelled) {
    PlaybackSession session([](const Event&) { ADD_FAILURE(); return true; });
    std::vector<std::unique_ptr<Event>> events;
    for (int i = 0; i < 6400; ++i) events.push_back(std::make_unique<DelayEvent>());
    uint64_t id; const auto start = std::chrono::steady_clock::now();
    ASSERT_EQ(PlaybackResult::Running, session.start(std::move(events), false, id));
    EXPECT_EQ(PlaybackResult::Finished, finished(session, id));
    EXPECT_GE(std::chrono::steady_clock::now() - start, 90ms);
    events.push_back(std::make_unique<DelayEvent>());
    ASSERT_EQ(PlaybackResult::Running, session.start(std::move(events), true, id));
    EXPECT_EQ(PlaybackResult::Cancelled, session.abort(id));
}

TEST(ExtendedWait, NativeValidatesEachProviderAndCreatesOrdinaryWaitBoundary) {
    auto event = wait_event(); event->condition.set_semantics_version(2); event->condition.set_poll_interval_us(250000);
    auto text = event->condition.mutable_accessibility_text();
    text->mutable_target()->set_title("Fake"); text->mutable_element()->set_automation_id("status"); text->mutable_predicate()->set_expected("Ready");
    EXPECT_TRUE(WaitEvent::valid(event->condition));
    event->condition.set_poll_interval_us(100000); EXPECT_FALSE(WaitEvent::valid(event->condition));
    event->condition.set_poll_interval_us(500000);
    auto ocr = event->condition.mutable_ocr_text(); ocr->set_language("eng"); ocr->mutable_region()->set_width(100); ocr->mutable_region()->set_height(100); ocr->mutable_predicate();
    EXPECT_TRUE(WaitEvent::valid(event->condition));
    ocr->set_language("../eng"); EXPECT_FALSE(WaitEvent::valid(event->condition)); ocr->set_language("eng");
    ocr->mutable_region()->set_width(4096); ocr->mutable_region()->set_height(4096); EXPECT_FALSE(WaitEvent::valid(event->condition));
    auto memory = event->condition.mutable_memory(); memory->set_executable_path("C:\\fake.exe"); memory->set_absolute_address(4096);
    memory->set_scalar_type(protobufGenerated::UINT64); memory->set_expected("18446744073709551615");
    EXPECT_TRUE(WaitEvent::valid(event->condition));
    memory->set_expected("18446744073709551616"); EXPECT_FALSE(WaitEvent::valid(event->condition));
    memory->set_scalar_type(protobufGenerated::INT64); memory->set_expected("-9223372036854775808"); EXPECT_TRUE(WaitEvent::valid(event->condition));
    memory->set_expected("-9223372036854775809"); EXPECT_FALSE(WaitEvent::valid(event->condition));
    memory->set_scalar_type(protobufGenerated::FLOAT64); memory->set_expected("NaN"); EXPECT_FALSE(WaitEvent::valid(event->condition));
    memory->set_expected("7.25"); memory->set_tolerance(.25); EXPECT_TRUE(WaitEvent::valid(event->condition));
    memory->set_comparison(protobufGenerated::LESS); EXPECT_FALSE(WaitEvent::valid(event->condition)); memory->set_tolerance(0);
    for (int i = 0; i < 17; ++i) memory->add_pointer_offsets(0);
    EXPECT_FALSE(WaitEvent::valid(event->condition)); memory->mutable_pointer_offsets()->RemoveLast(); EXPECT_TRUE(WaitEvent::valid(event->condition));
    const auto serialized = event->serialize(); EXPECT_NE(nullptr, dynamic_cast<WaitEvent*>(record_playback::deserialize_event(*serialized).get()));
    PlaybackSession session([](const Event&) { ADD_FAILURE() << "Wait injected input"; return true; }, true);
    std::vector<std::unique_ptr<Event>> events; events.push_back(std::move(event)); uint64_t id;
    ASSERT_EQ(PlaybackResult::Running, session.start(std::move(events), false, id));
    const auto active = request(session, id); ASSERT_NE(0u, active.occurrence);
    EXPECT_EQ(PlaybackResult::Running, session.resolve_wait(id, active.occurrence, true));
    EXPECT_EQ(PlaybackResult::Finished, finished(session, id));
}

TEST(FixedDelay, OversizedCombinedScheduleRejectsBeforeAnySinkCall) {
    PlaybackSession session([](const Event&) { ADD_FAILURE(); return true; });
    auto delay = std::make_unique<DelayEvent>(); delay->duration = 24h;
    delay->time_since_last_event = (std::chrono::microseconds::max)();
    std::vector<std::unique_ptr<Event>> events; events.push_back(std::move(delay)); uint64_t id;
    EXPECT_EQ(PlaybackResult::InvalidInput, session.start(std::move(events), false, id));
}

TEST(ExtendedWait, ScalarSignGrammarMatchesManaged) {
    auto event = wait_event(); event->condition.set_semantics_version(2); event->condition.set_poll_interval_us(100000);
    auto memory = event->condition.mutable_memory(); memory->set_executable_path("C:\\fake.exe"); memory->set_absolute_address(4096);
    for (const auto type : { protobufGenerated::INT64, protobufGenerated::UINT64, protobufGenerated::FLOAT64 }) {
        memory->set_scalar_type(type);
        for (const auto value : { "+-1", "++1", "-+1", "--1" }) { memory->set_expected(value); EXPECT_FALSE(WaitEvent::valid(event->condition)) << value; }
        for (const auto value : { "+1", "-0", "0" }) { memory->set_expected(value); EXPECT_TRUE(WaitEvent::valid(event->condition)) << value; }
    }
    for (const auto value : { "1e+2", "1e-2", "+1.0e-2" }) { memory->set_expected(value); EXPECT_TRUE(WaitEvent::valid(event->condition)) << value; }
    for (const auto value : { "1e+-2", "1e++2", "1e--2" }) { memory->set_expected(value); EXPECT_FALSE(WaitEvent::valid(event->condition)) << value; }
}

TEST(ExtendedWait, ScalarNulRejectionMatchesManagedForEveryType) {
    auto event = wait_event(); event->condition.set_semantics_version(2); event->condition.set_poll_interval_us(100000);
    auto memory = event->condition.mutable_memory(); memory->set_executable_path("C:\\fake.exe"); memory->set_absolute_address(4096);
    for (const auto type : { protobufGenerated::UINT8, protobufGenerated::INT8, protobufGenerated::UINT16, protobufGenerated::INT16,
        protobufGenerated::UINT32, protobufGenerated::INT32, protobufGenerated::UINT64, protobufGenerated::INT64,
        protobufGenerated::FLOAT32, protobufGenerated::FLOAT64 }) {
        SCOPED_TRACE(static_cast<int>(type));
        memory->set_scalar_type(type); memory->set_expected("1");
        ASSERT_TRUE(WaitEvent::valid(event->condition));
        for (const auto& value : { std::string("\0" "1", 2), std::string("1\0", 2), std::string("1\0\0", 3), std::string("1\0" "2", 3) }) {
            memory->set_expected(value);
            EXPECT_FALSE(WaitEvent::valid(event->condition));
            EXPECT_THROW(record_playback::deserialize_event(*event->serialize()), std::invalid_argument);
        }
    }
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

TEST(ConditionalWait, UnobservedOneMillisecondTimeoutRetainsTerminalIdentityOnlyForItsSession) {
    PlaybackSession session([](const Event&) { return true; });
    auto events = only_wait(1ms); events.insert(events.begin(), key_event(true));
    uint64_t id; ASSERT_EQ(PlaybackResult::Running, session.start(std::move(events), false, id));
    ASSERT_EQ(PlaybackResult::WaitTimedOut, finished(session, id));
    PlaybackWaitRequest terminal{};
    EXPECT_EQ(PlaybackResult::WaitTimedOut, session.wait_request(id, terminal));
    ASSERT_NE(0u, terminal.occurrence); EXPECT_EQ(1u, terminal.event_index); EXPECT_EQ(0u, terminal.remaining_us);
    EXPECT_EQ(PlaybackResult::StaleWait, session.resolve_wait(id, terminal.occurrence, true));
    uint64_t next; ASSERT_EQ(PlaybackResult::Running, session.start(only_wait(1ms), false, next));
    EXPECT_EQ(PlaybackResult::StaleSession, session.wait_request(id, terminal)); EXPECT_EQ(0u, terminal.occurrence);
    ASSERT_EQ(PlaybackResult::WaitTimedOut, finished(session, next));
    EXPECT_EQ(PlaybackResult::WaitTimedOut, session.wait_request(next, terminal)); EXPECT_EQ(0u, terminal.event_index);
}

TEST(ConditionalWait, ObservedSuccessDoesNotHideSubsequentUnobservedTimeout) {
    PlaybackSession session([](const Event&) { ADD_FAILURE() << "Wait-only input"; return true; });
    auto events = only_wait(); events.push_back(wait_event(1ms));
    uint64_t id; ASSERT_EQ(PlaybackResult::Running, session.start(std::move(events), false, id));
    const auto first = request(session, id); ASSERT_NE(0u, first.occurrence);
    EXPECT_EQ(PlaybackResult::Running, session.resolve_wait(id, first.occurrence, true));
    ASSERT_EQ(PlaybackResult::WaitTimedOut, finished(session, id));
    PlaybackWaitRequest terminal{};
    EXPECT_EQ(PlaybackResult::WaitTimedOut, session.wait_request(id, terminal));
    EXPECT_GT(terminal.occurrence, first.occurrence); EXPECT_EQ(1u, terminal.event_index); EXPECT_EQ(0u, terminal.remaining_us);
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

TEST(ConditionalWait, ChangesRejectsImpossibleExistsAndAbsentButAcceptsVisibility) {
    auto event = wait_event(); event->condition.set_trigger(protobufGenerated::CHANGES);
    EXPECT_FALSE(WaitEvent::valid(event->condition));
    event->condition.mutable_window()->set_test(protobufGenerated::ABSENT); EXPECT_FALSE(WaitEvent::valid(event->condition));
    event->condition.mutable_window()->set_test(protobufGenerated::VISIBLE); EXPECT_TRUE(WaitEvent::valid(event->condition));
    event->condition.mutable_window()->set_test(protobufGenerated::FOREGROUND); EXPECT_TRUE(WaitEvent::valid(event->condition));
}
