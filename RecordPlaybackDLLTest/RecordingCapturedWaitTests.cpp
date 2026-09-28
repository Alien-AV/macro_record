#include "pch.h"
#include "../RecordPlaybackDLL/Record/RecordingPipeline.h"
#include <future>

using namespace record_playback::capture;
namespace {
constexpr CaptureGesture capture_x{MOD_CONTROL, 'X'};
std::unique_ptr<WaitEvent> condition(uint32_t rgb = 1) {
    auto wait = std::make_unique<WaitEvent>();
    wait->condition.set_semantics_version(1);
    wait->condition.set_timeout_us(1000000);
    wait->condition.set_poll_interval_us(10000);
    wait->condition.mutable_pixel()->set_rgb(rgb);
    return wait;
}
struct CaptureFixture {
    int64_t now = 0;
    std::vector<Packet> received;
    Pipeline pipeline{[&](Packet packet) { received.push_back(std::move(packet)); }, [] { return false; },
        [&] { return std::chrono::steady_clock::time_point(std::chrono::microseconds(now)); }};
    void start(uint64_t id = 1) { ASSERT_TRUE(pipeline.start(id, ControlW, {-12, 21, true}, {capture_x})); }
    void key(WORD key, bool up, int64_t delay, DWORD time) {
        now += delay;
        RAWKEYBOARD raw{}; raw.VKey = key; raw.Flags = up ? RI_KEY_BREAK : 0;
        pipeline.keyboard(raw, time);
    }
    void move(int64_t delay, DWORD time, USHORT flags = 0) {
        now += delay; RAWMOUSE raw{}; raw.lLastX = 7; raw.usButtonFlags = flags;
        pipeline.mouse(raw, {}, time);
    }
    void resolve(DWORD time, uint32_t rgb = 1, uint64_t session = 1) { pipeline.captured_wait(session, capture_x, time, condition(rgb)); }
    void drain() { while (pipeline.collect_one()) {} }
    std::vector<Event*> events() {
        std::vector<Event*> result;
        for (auto& packet : received) if (packet.event) result.push_back(packet.event.get());
        return result;
    }
    size_t count(Boundary boundary) {
        return std::count_if(received.begin(), received.end(), [&](const Packet& packet) { return packet.boundary == boundary; });
    }
};
}

TEST(RecordingCapturedWait, BackloggedSamplesRetainTriggerOrderMotionAndExactTiming) {
    CaptureFixture f; f.start();
    f.key(VK_CONTROL, false, 10, 1); f.move(20, 2); f.key('X', false, 30, 3);
    f.key('X', true, 40, 4); f.key('X', false, 50, 5);
    f.key('X', true, 60, 6); f.key(VK_CONTROL, true, 70, 7); f.move(80, 8);
    f.resolve(5, 2); f.resolve(3, 1); // UI completion order cannot reorder reservations.
    f.pipeline.stop(1); f.drain();
    const auto events = f.events(); ASSERT_EQ(4u, events.size());
    EXPECT_NE(nullptr, dynamic_cast<MouseEvent*>(events[0]));
    ASSERT_NE(nullptr, dynamic_cast<WaitEvent*>(events[1]));
    ASSERT_NE(nullptr, dynamic_cast<WaitEvent*>(events[2]));
    EXPECT_EQ(1u, static_cast<WaitEvent*>(events[1])->condition.pixel().rgb());
    EXPECT_EQ(2u, static_cast<WaitEvent*>(events[2])->condition.pixel().rgb());
    EXPECT_EQ(30, events[0]->time_since_last_event.count());
    EXPECT_EQ(30, events[1]->time_since_last_event.count());
    EXPECT_EQ(90, events[2]->time_since_last_event.count());
    EXPECT_EQ(210, events[3]->time_since_last_event.count());
    EXPECT_EQ(1u, f.count(Boundary::Stopped));
    EXPECT_EQ(-12, f.received.front().origin.x);
}

TEST(RecordingCapturedWait, PreviouslyPublishedModifierKeepsItsReleaseAndRejectsWait) {
    CaptureFixture f; f.start();
    f.key(VK_CONTROL, false, 10, 1); f.key('A', false, 20, 2); f.key('A', true, 30, 3);
    f.move(40, 4); f.key('X', false, 50, 5); f.key('X', true, 60, 6);
    f.key(VK_CONTROL, true, 70, 7); f.move(80, 8);
    f.resolve(5); f.pipeline.stop(1); f.drain();
    const auto events = f.events(); ASSERT_EQ(6u, events.size());
    const auto down = dynamic_cast<KeyboardEvent*>(events[0]);
    const auto up = dynamic_cast<KeyboardEvent*>(events[4]);
    ASSERT_NE(nullptr, down); ASSERT_NE(nullptr, up);
    EXPECT_EQ(VK_LCONTROL, down->virtualKeyCode); EXPECT_FALSE(down->keyUp);
    EXPECT_EQ(VK_LCONTROL, up->virtualKeyCode); EXPECT_TRUE(up->keyUp);
    EXPECT_EQ(180, up->time_since_last_event.count());
    EXPECT_EQ(1u, f.count(Boundary::WaitHeldInput));
    for (auto event : events) EXPECT_EQ(nullptr, dynamic_cast<WaitEvent*>(event));
}

TEST(RecordingCapturedWait, IdleHeldModifierAndMouseButtonsRejectWithoutClaimingTheirRelease) {
    for (bool mouse : {false, true}) {
        CaptureFixture f;
        if (mouse) f.move(1, 0, RI_MOUSE_LEFT_BUTTON_DOWN);
        else f.key(VK_CONTROL, false, 1, 0);
        f.start();
        if (mouse) f.key(VK_CONTROL, false, 10, 1);
        f.key('X', false, 20, 2); f.key('X', true, 30, 3); f.key(VK_CONTROL, true, 40, 4);
        if (mouse) f.move(50, 5, RI_MOUSE_LEFT_BUTTON_UP);
        f.pipeline.stop(1); f.drain();
        EXPECT_EQ(1u, f.count(Boundary::WaitHeldInput));
        bool release = false;
        for (auto event : f.events()) {
            EXPECT_EQ(nullptr, dynamic_cast<WaitEvent*>(event));
            if (!mouse) { auto key = dynamic_cast<KeyboardEvent*>(event); release |= key && key->keyUp && key->virtualKeyCode == VK_LCONTROL; }
            else { auto input = dynamic_cast<MouseEvent*>(event); release |= input && (input->ActionType & MOUSEEVENTF_LEFTUP); }
        }
        EXPECT_TRUE(release);
    }
}

TEST(RecordingCapturedWait, CancellationAndStopResolveReservationsAndPreserveFollowingDelay) {
    for (bool explicit_cancel : {false, true}) {
        CaptureFixture f; f.start();
        f.key(VK_CONTROL, false, 10, 1); f.key('X', false, 20, 2);
        f.key('X', true, 30, 3); f.key(VK_CONTROL, true, 40, 4); f.move(50, 5);
        if (explicit_cancel) f.pipeline.captured_wait(1, capture_x, 2, nullptr);
        f.pipeline.stop(1); f.drain();
        ASSERT_EQ(1u, f.events().size()); EXPECT_EQ(150, f.events()[0]->time_since_last_event.count());
        EXPECT_EQ(1u, f.count(Boundary::WaitCancelled)); EXPECT_EQ(1u, f.count(Boundary::Stopped));
    }
}

TEST(RecordingCapturedWait, StopChordSharingSuppressedControlNeverLeaksKeysWithMotion) {
    CaptureFixture f; f.start();
    f.key(VK_CONTROL, false, 10, 1); f.move(20, 2); f.key('X', false, 30, 3); f.resolve(3);
    f.key('X', true, 40, 4); f.move(50, 5); f.key('W', false, 60, 6);
    f.move(70, 7); f.key('W', true, 80, 8); f.key(VK_CONTROL, true, 90, 9);
    f.pipeline.stop(1, ControlW, 6); f.drain();
    ASSERT_EQ(4u, f.events().size());
    for (auto event : f.events()) EXPECT_EQ(nullptr, dynamic_cast<KeyboardEvent*>(event));
    EXPECT_EQ(130, f.events().back()->time_since_last_event.count());
}

TEST(RecordingCapturedWait, StopCandidateOwnershipCannotBeStolenByCapture) {
    CaptureFixture f; f.start();
    f.key(VK_CONTROL, false, 10, 1); f.key('W', false, 20, 2); f.key('W', true, 30, 3);
    f.key('X', false, 40, 4); f.key('X', true, 50, 5); f.key(VK_CONTROL, true, 60, 6);
    f.pipeline.stop(1); f.drain();
    ASSERT_EQ(4u, f.events().size());
    EXPECT_EQ(1u, f.count(Boundary::WaitHeldInput));
    EXPECT_TRUE(static_cast<KeyboardEvent*>(f.events().back())->keyUp);
    EXPECT_EQ(VK_LCONTROL, static_cast<KeyboardEvent*>(f.events().back())->virtualKeyCode);
}

TEST(RecordingCapturedWait, RolloverPreservesSuppressionButResetsTimingAndRejectsOldSession) {
    CaptureFixture f; f.start();
    f.key(VK_CONTROL, false, 10, 1); f.key('X', false, 20, 2);
    f.pipeline.stop(1); f.start(2);
    f.resolve(2, 1, 1); f.key('X', true, 30, 3); f.key(VK_CONTROL, true, 40, 4); f.move(50, 5);
    f.pipeline.stop(2); f.drain();
    ASSERT_EQ(1u, f.events().size()); EXPECT_EQ(120, f.events()[0]->time_since_last_event.count());
    EXPECT_EQ(1u, f.count(Boundary::WaitCancelled)); EXPECT_EQ(2u, f.count(Boundary::Stopped));
    EXPECT_EQ(2u, f.received[f.received.size() - 2].session);
}

TEST(RecordingCapturedWait, TimestampWrapRepeatedNotificationsAndAmbiguousSameTickAreSafe) {
    CaptureFixture f; f.start();
    f.key(VK_CONTROL, false, 10, 0xfffffffe); f.key('X', false, 10, 0xffffffff);
    f.key('X', false, 10, 0xffffffff); // typematic, no second reservation
    f.key('X', true, 10, 0); f.key('X', false, 10, 1);
    f.resolve(0xffffffff, 1); f.resolve(0xffffffff, 9); f.resolve(1, 2);
    f.key('X', true, 10, 1); f.key('X', false, 10, 1); // indistinguishable timestamp: reject
    f.resolve(1, 9); f.pipeline.stop(1); f.drain();
    ASSERT_EQ(2u, f.events().size());
    EXPECT_EQ(1u, static_cast<WaitEvent*>(f.events()[0])->condition.pixel().rgb());
    EXPECT_EQ(2u, static_cast<WaitEvent*>(f.events()[1])->condition.pixel().rgb());
    EXPECT_EQ(3u, f.count(Boundary::WaitStale));
}

TEST(RecordingCapturedWait, ExhaustedDeliveryFailsAndUnblocksOutstandingMarker) {
    CaptureFixture f; f.start();
    f.key(VK_CONTROL, false, 10, 1); f.key('X', false, 10, 2);
    for (DWORD time = 3; time < 800; ++time) f.pipeline.captured_wait(1, capture_x, time, nullptr);
    EXPECT_EQ(0u, f.pipeline.session());
    f.drain();
    EXPECT_EQ(1u, f.count(Boundary::Failed)); EXPECT_EQ(1u, f.count(Boundary::WaitCancelled));
    f.start(2); f.move(10, 900); f.pipeline.stop(2); f.drain();
    EXPECT_EQ(1u, f.count(Boundary::Stopped));
}

TEST(RecordingCapturedWait, NoResponseDeadlineRejectsWithoutWaitingAndIgnoresLateResolution) {
    WaitMarker marker(capture_x, 1, std::chrono::milliseconds(0));
    auto result = marker.await();
    EXPECT_EQ(CaptureResult::TimedOut, result.result); EXPECT_FALSE(result.event);
    EXPECT_FALSE(marker.resolve(condition(), CaptureResult::Inserted));
}

TEST(RecordingCapturedWait, ExpiredReservationRejectsResolutionBeforeCollectorAwait) {
    WaitMarker marker(capture_x, 1, std::chrono::milliseconds(0));
    EXPECT_FALSE(marker.resolve(condition(), CaptureResult::Inserted));
    EXPECT_TRUE(marker.ready());
    const auto result = marker.await();
    EXPECT_EQ(CaptureResult::TimedOut, result.result);
    EXPECT_FALSE(result.event);
    EXPECT_FALSE(marker.resolve(condition(), CaptureResult::Inserted));
}

TEST(RecordingCapturedWait, InvalidOrCollidingSessionConfigurationsFailClosed) {
    const std::vector<std::vector<CaptureGesture>> invalid = {
        {capture_x, capture_x}, {{MOD_CONTROL | MOD_NOREPEAT, 'X'}}, {{MOD_CONTROL, VK_CONTROL}},
        {{0, 'X'}}, {{MOD_CONTROL, 256}}, {{MOD_CONTROL, VK_LBUTTON}}, {{MOD_CONTROL, VK_PACKET}},
        {{MOD_CONTROL, 255}}, {{MOD_CONTROL, 'Q'}}, {{MOD_CONTROL, 'W'}}
    };
    for (const auto& gestures : invalid) {
        CaptureFixture f;
        EXPECT_FALSE(f.pipeline.start(1, ControlW, {}, gestures)); f.drain();
        EXPECT_EQ(1u, f.count(Boundary::Failed)); EXPECT_EQ(0u, f.pipeline.session());
    }
}

TEST(RecordingCapturedWait, HeldCommandModifierReusedForGenuineInputTransfersReleaseOwnership) {
    CaptureFixture f; f.start();
    f.key(VK_CONTROL, false, 10, 1); f.key('X', false, 20, 2); f.resolve(2);
    f.key('X', true, 30, 3); f.key('A', false, 40, 4); f.key('A', true, 50, 5);
    f.key('X', false, 60, 6); f.key('X', true, 70, 7); f.key(VK_CONTROL, true, 80, 8);
    f.pipeline.stop(1); f.drain();
    const auto events = f.events(); ASSERT_EQ(5u, events.size());
    ASSERT_NE(nullptr, dynamic_cast<WaitEvent*>(events[0]));
    const auto down = dynamic_cast<KeyboardEvent*>(events[1]);
    const auto action = dynamic_cast<KeyboardEvent*>(events[2]);
    const auto up = dynamic_cast<KeyboardEvent*>(events[4]);
    ASSERT_NE(nullptr, down); ASSERT_NE(nullptr, action); ASSERT_NE(nullptr, up);
    EXPECT_EQ(VK_LCONTROL, down->virtualKeyCode); EXPECT_FALSE(down->keyUp);
    EXPECT_EQ(70, down->time_since_last_event.count()); EXPECT_EQ(0, action->time_since_last_event.count());
    EXPECT_EQ(VK_LCONTROL, up->virtualKeyCode); EXPECT_TRUE(up->keyUp);
    EXPECT_EQ(210, up->time_since_last_event.count());
    EXPECT_EQ(1u, f.count(Boundary::WaitHeldInput));
}

TEST(RecordingCapturedWait, WaitingCollectorCannotBlockRawMotionAndStopCancelsIt) {
    CaptureFixture f; f.start();
    f.key(VK_CONTROL, false, 10, 1); f.key('X', false, 20, 2);
    ASSERT_TRUE(f.pipeline.collect_one()); // Started
    ASSERT_TRUE(f.pipeline.collect_one()); // omitted provisional Ctrl
    auto collector = std::async(std::launch::async, [&] { return f.pipeline.collect_one(); });
    for (DWORD time = 3; time < 603; ++time) f.move(1, time);
    EXPECT_EQ(630, f.now);
    f.pipeline.stop(1);
    ASSERT_EQ(std::future_status::ready, collector.wait_for(std::chrono::seconds(2)));
    ASSERT_TRUE(collector.get());
    f.drain();
    EXPECT_EQ(600u, f.events().size());
    EXPECT_EQ(1u, f.count(Boundary::WaitCancelled));
    EXPECT_EQ(31, f.events().front()->time_since_last_event.count());
    EXPECT_EQ(1u, f.count(Boundary::Stopped));
}
