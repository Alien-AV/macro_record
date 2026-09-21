#include "pch.h"
#include "../RecordPlaybackDLL/Record/RecordingStream.h"
#include <deque>
#include <limits>

using namespace record_playback::capture;

namespace {
std::unique_ptr<KeyboardEvent> keyboard(WORD code, bool up = false, int64_t delay = 7) {
    auto key = std::make_unique<KeyboardEvent>();
    key->virtualKeyCode = code;
    key->hardwareScanCode = 0x1234;
    key->keyUp = up;
    key->time_since_last_event = std::chrono::microseconds(delay);
    return key;
}
std::unique_ptr<MouseEvent> motion(int i = 0, int64_t delay = 11) {
    auto mouse = std::make_unique<MouseEvent>(-3000 - i, 400 + i, MOUSEEVENTF_MOVE | MOUSEEVENTF_MOVE_NOCOALESCE,
        0x87654321, i % 2 == 0, i % 3 == 0);
    mouse->time_since_last_event = std::chrono::microseconds(delay);
    return mouse;
}
void key(Stream& stream, WORD code, bool up = false, int64_t delay = 7, DWORD time = 1) {
    stream.key(code, up);
    stream.input(keyboard(code, up, delay), time);
}
void check_motion(const Event* event, int i, int64_t delay) {
    const auto mouse = dynamic_cast<const MouseEvent*>(event);
    ASSERT_NE(nullptr, mouse);
    EXPECT_EQ(-3000 - i, mouse->x);
    EXPECT_EQ(400 + i, mouse->y);
    EXPECT_EQ(MOUSEEVENTF_MOVE | MOUSEEVENTF_MOVE_NOCOALESCE, mouse->ActionType);
    EXPECT_EQ(0x87654321u, mouse->wheelRotation);
    EXPECT_EQ(i % 2 == 0, mouse->mappedToVirtualDesktop);
    EXPECT_EQ(i % 3 == 0, mouse->relative_position);
    EXPECT_EQ(std::chrono::microseconds(delay), mouse->time_since_last_event);
}
}

TEST(RecordingMotion, ConfirmedGesturesRetainLongInterleavingsBeforeAndAfterTriggerWithExactTiming) {
    for (auto gesture : {ControlW, ControlR, ControlAltF12, ControlShiftF12})
    for (bool trigger : {false, true}) {
        std::vector<Packet> received;
        Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
        stream.start(1, gesture);
        key(stream, VK_LCONTROL, false, 7);
        constexpr int samples = 8193;
        for (int i = 0; i < samples; ++i) {
            if (i == 1000) key(stream, VK_LCONTROL, false, 13);
            if (i == 2000 && (gesture == ControlAltF12 || gesture == ControlShiftF12))
                key(stream, gesture == ControlAltF12 ? VK_RMENU : VK_RSHIFT, false, 17);
            if (i == 4000 && trigger) key(stream, gesture == ControlW ? 'W' : gesture == ControlR ? 'R' : VK_F12, false, 19);
            stream.input(motion(i, i + 1), 1);
        }
        EXPECT_EQ(1u, received.size()); // Nothing provisional escapes under pressure.
        stream.stop(1, gesture, 1);
        ASSERT_EQ(samples + 2u, received.size());
        for (int i = 0; i < samples; ++i) {
            const auto delay = i + 1 + (i == 0 ? 7 : 0) + (i == 1000 ? 13 : 0)
                + (i == 2000 && (gesture == ControlAltF12 || gesture == ControlShiftF12) ? 17 : 0)
                + (i == 4000 && trigger ? 19 : 0);
            check_motion(received[i + 1].event.get(), i, delay);
        }
        EXPECT_EQ(Boundary::Stopped, received.back().boundary);
    }
}

TEST(RecordingMotion, LongCandidateRequiresSuccessfulMatchingRegisteredSessionAndTimestamp) {
    for (int scenario = 0; scenario < 6; ++scenario) {
        std::vector<Packet> received;
        Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
        stream.start(5, scenario == 1 ? NoStopGesture : ControlW | ControlR);
        key(stream, VK_RCONTROL, false, 17, 100);
        for (int i = 0; i < 4097; ++i) stream.input(motion(i, i + 31), 100);
        key(stream, 'W', false, 23, scenario == 5 ? 102 : 100);
        if (scenario == 3) {
            stream.stop(4, ControlW, 100);
            EXPECT_EQ(5u, stream.session());
        }
        stream.stop(5, scenario == 0 || scenario == 3 ? NoStopGesture : scenario == 2 ? ControlR : ControlW,
            scenario == 4 ? 99 : 100);
        ASSERT_EQ(4101u, received.size());
        const auto ctrl = dynamic_cast<KeyboardEvent*>(received[1].event.get());
        ASSERT_NE(nullptr, ctrl);
        EXPECT_EQ(VK_RCONTROL, ctrl->virtualKeyCode);
        EXPECT_EQ(0x1234, ctrl->hardwareScanCode);
        EXPECT_EQ(std::chrono::microseconds(17), ctrl->time_since_last_event);
        for (int i = 0; i < 4097; ++i) check_motion(received[i + 2].event.get(), i, i + 31);
        const auto trigger = dynamic_cast<KeyboardEvent*>(received[4099].event.get());
        ASSERT_NE(nullptr, trigger);
        EXPECT_EQ('W', trigger->virtualKeyCode);
        EXPECT_EQ(std::chrono::microseconds(23), trigger->time_since_last_event);
    }
}

TEST(RecordingMotion, ModifiedButtonsWheelsAndTypingCancelLongCandidateAndRetainOriginalOrder) {
    const DWORD interactions[] = {MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, MOUSEEVENTF_RIGHTDOWN,
        MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, MOUSEEVENTF_WHEEL, MOUSEEVENTF_HWHEEL, 0};
    for (bool after_trigger : {false, true}) for (auto interaction : interactions) {
        std::vector<Packet> received;
        Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
        stream.start(1, ControlW);
        key(stream, VK_CONTROL, false, 17);
        for (int i = 0; i < 1025; ++i) stream.input(motion(i, 31), 1);
        if (after_trigger) key(stream, 'W', false, 43);
        if (interaction) {
            auto mouse = motion(3000, 47);
            mouse->ActionType |= interaction;
            mouse->wheelRotation = interaction == MOUSEEVENTF_XDOWN || interaction == MOUSEEVENTF_XUP ? XBUTTON2 : DWORD(-120);
            stream.input(std::move(mouse), 1);
        } else key(stream, 'C', false, 47);
        key(stream, VK_CONTROL, false, 53);
        if (!after_trigger) key(stream, 'W', false, 43);
        stream.stop(1, ControlW, 1);
        // With C still held, W is also genuine. A W already disambiguated by
        // later input remains genuine; other cases may omit the final W only.
        ASSERT_EQ(after_trigger || !interaction ? 1031u : 1030u, received.size());
        const auto ctrl = dynamic_cast<KeyboardEvent*>(received[1].event.get());
        ASSERT_NE(nullptr, ctrl);
        EXPECT_EQ(std::chrono::microseconds(17), ctrl->time_since_last_event);
        for (int i = 0; i < 1025; ++i) check_motion(received[i + 2].event.get(), i, 31);
    }
}

TEST(RecordingMotion, HeldButtonsIncludingIdleDragPreventFreshModifierClaim) {
    for (bool idle : {false, true}) for (USHORT button : {RI_MOUSE_LEFT_BUTTON_DOWN, RI_MOUSE_RIGHT_BUTTON_DOWN,
        RI_MOUSE_MIDDLE_BUTTON_DOWN, RI_MOUSE_BUTTON_4_DOWN, RI_MOUSE_BUTTON_5_DOWN}) {
        std::vector<Packet> received;
        Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
        if (idle) stream.mouse(button);
        stream.start(1, ControlW);
        if (!idle) {
            const DWORD flag = button == RI_MOUSE_LEFT_BUTTON_DOWN ? MOUSEEVENTF_LEFTDOWN
                : button == RI_MOUSE_RIGHT_BUTTON_DOWN ? MOUSEEVENTF_RIGHTDOWN
                : button == RI_MOUSE_MIDDLE_BUTTON_DOWN ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_XDOWN;
            stream.input(std::make_unique<MouseEvent>(0, 0, flag, button == RI_MOUSE_BUTTON_5_DOWN ? XBUTTON2 : XBUTTON1, false, true));
        }
        key(stream, VK_LCONTROL);
        for (int i = 0; i < 513; ++i) stream.input(motion(i), 1);
        key(stream, 'W');
        stream.stop(1, ControlW, 1);
        ASSERT_EQ(idle ? 516u : 517u, received.size());
        EXPECT_NE(nullptr, dynamic_cast<KeyboardEvent*>(received[idle ? 1 : 2].event.get()));
    }
}

TEST(RecordingMotion, ReleasedIdleButtonsDoNotContaminateNextSessionAndHeldKeysStayGenuine) {
    std::vector<Packet> received;
    Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
    stream.mouse(RI_MOUSE_BUTTON_5_DOWN);
    stream.mouse(RI_MOUSE_BUTTON_5_UP);
    stream.key(VK_RCONTROL, false);
    stream.start(1, ControlW);
    key(stream, VK_RCONTROL, false, 3);
    stream.input(motion(0, 5), 1);
    key(stream, 'W', false, 7);
    key(stream, VK_RCONTROL, false, 11);
    key(stream, 'W', true, 13);
    key(stream, VK_RCONTROL, true, 17);
    stream.input(motion(1, 19), 1);
    stream.stop(1, ControlW, 1);
    ASSERT_EQ(7u, received.size());
    EXPECT_EQ(std::chrono::microseconds(3), received[1].event->time_since_last_event);
    check_motion(received[2].event.get(), 0, 5);
    EXPECT_EQ(std::chrono::microseconds(18), received[3].event->time_since_last_event);
    EXPECT_EQ(std::chrono::microseconds(30), received[4].event->time_since_last_event);
    check_motion(received[5].event.get(), 1, 19);
}

TEST(RecordingMotion, ReleaseThenRepressIsANewInteractionNotACommandRepeat) {
    std::vector<Packet> received;
    Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
    stream.start(1, ControlW);
    key(stream, VK_CONTROL);
    stream.input(motion(), 1);
    key(stream, 'W');
    key(stream, 'W', true);
    key(stream, 'W');
    stream.stop(1, ControlW, 1);
    EXPECT_EQ(7u, received.size());
}

TEST(RecordingMotion, TickWrapAndFiniteQueueDrainRetainAllMotionBeforeHotkeyCutoff) {
    std::vector<Packet> received;
    Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
    stream.start(1, ControlW);
    key(stream, VK_CONTROL, false, 7, 0xfffffff0);
    std::deque<DWORD> raw(2049, 0xfffffffe);
    raw.push_back(2);
    int consumed = 0;
    bool done;
    do {
        int batch = 0;
        done = drain_prefix(1, [&](DWORD& time) { if (raw.empty()) return false; time = raw.front(); return true; },
            [&] { stream.input(motion(consumed++), raw.front()); raw.pop_front(); ++batch; });
        EXPECT_LE(batch, 256);
    } while (!done);
    stream.stop(1, ControlW, 1);
    ASSERT_EQ(2051u, received.size());
    ASSERT_EQ(1u, raw.size());
    EXPECT_EQ(2u, raw.front());
    check_motion(received[1].event.get(), 0, 18);
    for (int i = 1; i < consumed; ++i) check_motion(received[i + 1].event.get(), i, 11);
}

TEST(RecordingMotion, UnknownPayloadCancelsMatchedCandidateWithoutLosingOwnershipOrData) {
    class OpaqueEvent : public Event {
    public:
        std::unique_ptr<std::vector<unsigned char>> serialize() const override {
            return std::make_unique<std::vector<unsigned char>>(std::initializer_list<unsigned char>{0xff, 0, 0x81});
        }
        void playback() const override { ADD_FAILURE() << "Must not inject input"; }
    };
    std::vector<Packet> received;
    Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
    stream.start(1, ControlW);
    key(stream, VK_CONTROL);
    for (int i = 0; i < 513; ++i) stream.input(motion(i), 1);
    key(stream, 'W');
    auto opaque = std::make_unique<OpaqueEvent>();
    opaque->time_since_last_event = std::chrono::microseconds(987654321);
    const auto original = opaque.get();
    stream.input(std::move(opaque), 1);
    stream.stop(1, ControlW, 1);
    ASSERT_EQ(518u, received.size());
    EXPECT_EQ(original, received[516].event.get());
    EXPECT_EQ(std::chrono::microseconds(987654321), original->time_since_last_event);
    EXPECT_EQ((std::vector<unsigned char>{0xff, 0, 0x81}), *original->serialize());
}

TEST(RecordingMotion, LongModifierTapCancelsThenFreshStopPrefixHasIndependentTiming) {
    std::vector<Packet> received;
    Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
    stream.start(1, ControlW);
    key(stream, VK_CONTROL, false, 7);
    for (int i = 0; i < 513; ++i) stream.input(motion(i), 1);
    key(stream, VK_CONTROL, true, 13);
    key(stream, VK_CONTROL, false, 17);
    stream.input(motion(513, 19), 1);
    stream.stop(1, ControlW, 1);
    ASSERT_EQ(518u, received.size());
    EXPECT_EQ(std::chrono::microseconds(7), received[1].event->time_since_last_event);
    for (int i = 0; i < 513; ++i) check_motion(received[i + 2].event.get(), i, 11);
    EXPECT_EQ(std::chrono::microseconds(13), received[515].event->time_since_last_event);
    check_motion(received[516].event.get(), 513, 36);
}

TEST(RecordingMotion, TimelyCommandStillOwnsLaterRepeatsAndReleasesOfTheSamePress) {
    for (bool trigger : {false, true}) {
        std::vector<Packet> received;
        Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
        stream.start(1, ControlW);
        key(stream, VK_CONTROL, false, 7, 100);
        stream.input(motion(0, 11), 100);
        if (trigger) key(stream, 'W', false, 13, 101);
        key(stream, VK_CONTROL, false, 17, 102);
        stream.input(motion(1, 19), 103);
        if (trigger) {
            key(stream, 'W', true, 23, 104);
            key(stream, VK_CONTROL, true, 29, 105);
            stream.input(motion(2, 31), 106);
        }
        stream.stop(1, ControlW, 101);
        ASSERT_EQ(trigger ? 5u : 4u, received.size());
        check_motion(received[1].event.get(), 0, 18);
        check_motion(received[2].event.get(), 1, trigger ? 49 : 36);
        if (trigger) check_motion(received[3].event.get(), 2, 83);
    }
}
