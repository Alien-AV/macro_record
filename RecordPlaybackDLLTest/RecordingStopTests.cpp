#include "pch.h"
#include "../RecordPlaybackDLL/Record/RecordingStream.h"
#include "../RecordPlaybackDLL/Common/KeyboardEvent.h"
#include "../RecordPlaybackDLL/Common/MouseEvent.h"
#include <deque>

using namespace record_playback::capture;

namespace {
void press(Stream& stream, WORD key, bool up = false, int delay = 1, DWORD time = 1) {
    auto event = std::make_unique<KeyboardEvent>();
    event->virtualKeyCode = key;
    event->keyUp = up;
    event->time_since_last_event = std::chrono::microseconds(delay);
    stream.key(key, up);
    stream.input(std::move(event), time);
}
}

TEST(RecordingStop, HotkeyBeforeRawTriggerDoesNotPublishFreshControl) {
    std::vector<Packet> received;
    Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
    stream.start(1, ControlW);
    press(stream, VK_LCONTROL);
    stream.stop(1, ControlW, 1);
    ASSERT_EQ(2u, received.size());
    EXPECT_EQ(Boundary::Started, received.front().boundary);
    EXPECT_EQ(Boundary::Stopped, received.back().boundary);
}

TEST(RecordingStop, RawTriggerAndRepeatsNeedActualHotkeyProvenance) {
    for (bool hotkey : {false, true}) for (WORD ctrl : {VK_CONTROL, VK_LCONTROL, VK_RCONTROL}) {
        std::vector<Packet> received;
        Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
        stream.start(1, ControlW);
        press(stream, 'A', false, 3);
        press(stream, 'A', true, 5);
        press(stream, ctrl, false, 7);
        press(stream, ctrl, false, 11);
        press(stream, 'W', false, 13);
        press(stream, 'W', false, 17);
        press(stream, 'W', true, 19);
        press(stream, ctrl, true, 23);
        stream.stop(1, hotkey ? ControlW : NoStopGesture, 1);
        ASSERT_EQ(hotkey ? 4u : 10u, received.size());
        EXPECT_EQ(std::chrono::microseconds(3), received[1].event->time_since_last_event);
        EXPECT_EQ(std::chrono::microseconds(5), received[2].event->time_since_last_event);
        if (!hotkey) {
            const int delays[] = {7, 11, 13, 17, 19, 23};
            for (size_t i = 0; i < 6; ++i)
                EXPECT_EQ(std::chrono::microseconds(delays[i]), received[i + 3].event->time_since_last_event);
        }
    }
}

TEST(RecordingStop, OrdinaryStopFlushesFreshIncompleteModifierWithoutTrigger) {
    std::vector<Packet> received;
    Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
    stream.start(1, ControlW | ControlR);
    press(stream, VK_CONTROL, false, 987654);
    ASSERT_EQ(1u, received.size());
    stream.stop(1);
    ASSERT_EQ(3u, received.size());
    EXPECT_EQ(std::chrono::microseconds(987654), received[1].event->time_since_last_event);
    EXPECT_FALSE(static_cast<KeyboardEvent*>(received[1].event.get())->keyUp);
}

TEST(RecordingStop, LegitimateTapAndShortcutStayExactBeforeFreshStopPrefix) {
    std::vector<Packet> received;
    Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
    stream.start(1, ControlW);
    press(stream, VK_CONTROL, false, 100);
    press(stream, VK_CONTROL, true, 200);
    press(stream, VK_CONTROL, false, 300);
    press(stream, 'C', false, 400);
    press(stream, 'C', true, 500);
    press(stream, VK_CONTROL, true, 600);
    press(stream, VK_RCONTROL, false, 700);
    stream.stop(1, ControlW, 1);
    ASSERT_EQ(8u, received.size());
    for (size_t i = 1; i <= 6; ++i)
        EXPECT_EQ(std::chrono::microseconds(i * 100), received[i].event->time_since_last_event);
}

TEST(RecordingStop, UsedModifierAndMouseDragStayTruthfullyIncomplete) {
    std::vector<Packet> received;
    Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
    stream.start(1, ControlW);
    press(stream, VK_LCONTROL, false, 10);
    auto mouse = std::make_unique<MouseEvent>(-8, 22, MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_MOVE, 0, true, true);
    mouse->time_since_last_event = std::chrono::microseconds(20);
    auto original = mouse.get();
    stream.input(std::move(mouse));
    press(stream, VK_LCONTROL, false, 30); // already-used repeat is genuine
    press(stream, 'W', false, 40);
    stream.stop(1, ControlW, 1);
    ASSERT_EQ(5u, received.size());
    EXPECT_EQ(original, received[2].event.get());
    EXPECT_EQ(std::chrono::microseconds(20), original->time_since_last_event);
    EXPECT_FALSE(static_cast<KeyboardEvent*>(received[1].event.get())->keyUp);
    EXPECT_FALSE(static_cast<KeyboardEvent*>(received[3].event.get())->keyUp);
}

TEST(RecordingStop, EmergencyVariantsRequireCurrentRegistrationAndMatchingCommand) {
    for (auto gesture : {ControlR, ControlAltF12, ControlShiftF12})
    for (bool registered : {false, true}) for (bool hotkey : {false, true}) for (bool reversed : {false, true}) {
        std::vector<Packet> received;
        Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
        stream.start(1, registered ? gesture : ControlW);
        const WORD extra = gesture == ControlAltF12 ? VK_RMENU : VK_RSHIFT;
        const bool multi = gesture != ControlR;
        if (multi && reversed) press(stream, extra);
        press(stream, VK_RCONTROL);
        if (multi && !reversed) press(stream, extra);
        press(stream, multi ? VK_F12 : 'R');
        stream.stop(1, hotkey ? gesture : NoStopGesture, 1);
        EXPECT_EQ(registered && hotkey ? 2u : multi ? 5u : 4u, received.size());
    }
}

TEST(RecordingStop, EmergencyCommandCanConfirmPrefixWithoutRawTrigger) {
    for (auto gesture : {ControlR, ControlAltF12, ControlShiftF12}) {
        std::vector<Packet> received;
        Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
        stream.start(1, gesture);
        press(stream, VK_LCONTROL);
        stream.stop(1, gesture, 1);
        EXPECT_EQ(2u, received.size());
    }
}

TEST(RecordingStop, ExtraModifiersAndPreHeldTriggerDoNotBecomeStopPrefixes) {
    for (WORD earlier : std::array<WORD, 4>{VK_LSHIFT, VK_LMENU, VK_LWIN, 'W'}) {
        std::vector<Packet> received;
        Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
        stream.start(1, ControlW);
        press(stream, earlier);
        press(stream, VK_LCONTROL);
        press(stream, 'W');
        stream.stop(1, ControlW, 1);
        EXPECT_EQ(5u, received.size());
    }
}

TEST(RecordingStop, BothControlSidesAndTheirRepeatsCanBelongToFreshCommand) {
    std::vector<Packet> received;
    Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
    stream.start(1, ControlW);
    press(stream, VK_LCONTROL);
    press(stream, VK_RCONTROL);
    press(stream, VK_LCONTROL);
    press(stream, 'W');
    stream.stop(1, ControlW, 1);
    EXPECT_EQ(2u, received.size());
}

TEST(RecordingStop, HeldAtStartAndItsRepeatsAreNotFreshCommandModifiers) {
    std::vector<Packet> received;
    Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
    stream.key(VK_LCONTROL, false);
    stream.start(1, ControlW);
    press(stream, VK_LCONTROL, false, 37);
    stream.stop(1, ControlW, 1);
    ASSERT_EQ(3u, received.size());
    EXPECT_EQ(std::chrono::microseconds(37), received[1].event->time_since_last_event);
}

TEST(RecordingStop, BufferPressurePreservesEveryRepeatAndItsExactDelay) {
    std::vector<std::unique_ptr<Event>> received;
    StopChord filter([&](std::unique_ptr<Event> event) { received.push_back(std::move(event)); });
    filter.start(ControlW, {});
    for (size_t i = 0; i < StopChord::capacity * 4; ++i) {
        auto event = std::make_unique<KeyboardEvent>();
        event->virtualKeyCode = VK_CONTROL;
        event->keyUp = false;
        event->time_since_last_event = std::chrono::microseconds(i * 19);
        filter.input(std::move(event), 1);
        ASSERT_LT(filter.pending_count(), StopChord::capacity);
    }
    filter.finish(ControlW, 1);
    ASSERT_EQ(StopChord::capacity * 4, received.size());
    for (size_t i = 0; i < received.size(); ++i)
        EXPECT_EQ(std::chrono::microseconds(i * 19), received[i]->time_since_last_event);
}

TEST(RecordingStop, OrdinaryStopPreservesUnknownEventPayloadAndOwnershipBehindCandidate) {
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
    press(stream, VK_CONTROL);
    press(stream, 'W');
    auto opaque = std::make_unique<OpaqueEvent>();
    opaque->time_since_last_event = std::chrono::microseconds(123456789);
    auto original = opaque.get();
    stream.input(std::move(opaque));
    stream.stop(1);
    ASSERT_EQ(5u, received.size());
    EXPECT_EQ(original, received[3].event.get());
    EXPECT_EQ(std::chrono::microseconds(123456789), original->time_since_last_event);
    EXPECT_EQ((std::vector<unsigned char>{0xff, 0, 0x81}), *original->serialize());
}

TEST(RecordingStop, LateTriggerAfterHotkeyCutoffStaysIdleAndCannotDrainNextSession) {
    std::vector<Packet> received;
    Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
    stream.start(1, ControlW);
    press(stream, VK_LCONTROL, false, 10, 10);
    std::deque<DWORD> raw{12};
    ASSERT_TRUE(drain_prefix(11, [&](DWORD& time) { if (raw.empty()) return false; time = raw.front(); return true; },
        [&] { press(stream, 'W', false, 20, raw.front()); raw.pop_front(); }));
    stream.stop(1, ControlW, 11);
    ASSERT_EQ(1u, raw.size());
    press(stream, 'W', false, 20, raw.front());
    press(stream, 'W', true);
    press(stream, VK_LCONTROL, true);
    stream.start(2, ControlR);
    press(stream, VK_LCONTROL, false, 33);
    stream.stop(1, ControlW, 11);
    EXPECT_EQ(2u, stream.session());
    press(stream, 'W', false, 44); // W is no longer registered for this capture
    stream.stop(2);
    ASSERT_EQ(6u, received.size());
    EXPECT_EQ(2u, received[3].session);
    EXPECT_EQ(std::chrono::microseconds(33), received[3].event->time_since_last_event);
    EXPECT_EQ(std::chrono::microseconds(44), received[4].event->time_since_last_event);
}

TEST(RecordingStop, CommandCannotClaimAPrefixNewerThanItsOriginalMessageTime) {
    std::vector<Packet> received;
    Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
    stream.start(1, ControlW);
    press(stream, VK_CONTROL, false, 10, 101);
    stream.stop(1, ControlW, 100);
    EXPECT_EQ(3u, received.size());
}

TEST(RecordingStop, RawAltAndShiftVariantsUsePhysicalSides) {
    RAWKEYBOARD raw{};
    raw.VKey = VK_MENU;
    EXPECT_EQ(VK_LMENU, sided_key(raw));
    raw.Flags = RI_KEY_E0;
    EXPECT_EQ(VK_RMENU, sided_key(raw));
    raw.VKey = VK_SHIFT;
    raw.MakeCode = 0x2a;
    EXPECT_EQ(VK_LSHIFT, sided_key(raw));
    raw.MakeCode = 0x36;
    EXPECT_EQ(VK_RSHIFT, sided_key(raw));
}
