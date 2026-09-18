#include "pch.h"
#include "../RecordPlaybackDLL/Record/RecordingStream.h"
#include "../RecordPlaybackDLL/Common/KeyboardEvent.h"
#include <deque>

using namespace record_playback::capture;

namespace {
std::unique_ptr<Event> key_event(WORD key, bool up) {
    auto event = std::make_unique<KeyboardEvent>();
    event->virtualKeyCode = key;
    event->keyUp = up;
    return event;
}
struct RawKey { DWORD time; WORD key; bool up; };
}

TEST(RecordingBoundary, OrderedStreamKeepsTailAndReadinessSeparateAcrossRapidSessions) {
    std::vector<Packet> received;
    Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
    stream.key(VK_LCONTROL, false);
    stream.key('Q', false);
    ASSERT_TRUE(stream.start(1));
    stream.input(key_event('Q', true));
    stream.key('Q', true);
    stream.stop(1);
    stream.key(VK_LCONTROL, true);
    stream.key(VK_RCONTROL, false);
    ASSERT_TRUE(stream.start(2));
    stream.input(key_event('A', false));
    stream.stop(1);
    EXPECT_EQ(2u, stream.session());
    stream.stop(2);
    ASSERT_EQ(6u, received.size());
    EXPECT_EQ(Boundary::Started, received[0].boundary);
    EXPECT_EQ(Q | LeftControl, received[0].held_keys);
    EXPECT_EQ(1u, received[1].session);
    ASSERT_NE(nullptr, received[1].event);
    EXPECT_EQ(Boundary::Stopped, received[2].boundary);
    EXPECT_EQ(Boundary::Started, received[3].boundary);
    EXPECT_EQ(RightControl, received[3].held_keys);
    EXPECT_EQ(2u, received[4].session);
    EXPECT_EQ(Boundary::Stopped, received[5].boundary);
}

TEST(RecordingBoundary, QueuedReleaseAfterCutoffDoesNotEraseHeldReadinessState) {
    std::vector<Packet> received;
    Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
    std::deque<RawKey> raw{{9, VK_LCONTROL, false}, {10, 'Q', false}, {12, 'Q', true}};
    const auto peek = [&](DWORD& time) { if (raw.empty()) return false; time = raw.front().time; return true; };
    const auto consume = [&] { auto key = raw.front(); raw.pop_front(); stream.key(key.key, key.up); };
    ASSERT_TRUE(drain_prefix(11, peek, consume));
    // Physical Q could already be up here, but its ordered raw release is still queued.
    stream.start(1);
    ASSERT_EQ(1u, raw.size());
    EXPECT_EQ(Q | LeftControl, received[0].held_keys);
    consume();
    EXPECT_EQ(LeftControl, stream.held_keys());
}

TEST(RecordingBoundary, ReleaseBeforeReadinessAndNextPressRetainTheirRawOrder) {
    std::vector<Packet> received;
    Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
    std::deque<RawKey> raw{{8, 'Q', false}, {9, 'Q', true}, {11, 'Q', false}};
    ASSERT_TRUE(drain_prefix(10,
        [&](DWORD& time) { if (raw.empty()) return false; time = raw.front().time; return true; },
        [&] { auto key = raw.front(); raw.pop_front(); stream.key(key.key, key.up); }));
    stream.start(1);
    EXPECT_EQ(0u, received[0].held_keys);
    ASSERT_EQ(1u, raw.size());
    stream.key(raw.front().key, raw.front().up);
    stream.input(key_event(raw.front().key, raw.front().up));
    ASSERT_EQ(2u, received.size());
    EXPECT_FALSE(static_cast<KeyboardEvent*>(received[1].event.get())->keyUp);
}

TEST(RecordingBoundary, HighRateProducerCannotExtendFixedPrefixOrExceedPerTurnBudget) {
    std::deque<DWORD> raw;
    for (DWORD time = 1; time <= 1000; ++time) raw.push_back(time);
    DWORD next = 1001;
    size_t consumed = 0;
    size_t turns = 0;
    bool complete;
    do {
        size_t this_turn = 0;
        complete = drain_prefix(600,
            [&](DWORD& time) { if (raw.empty()) return false; time = raw.front(); return true; },
            [&] { raw.pop_front(); raw.push_back(next++); ++this_turn; ++consumed; });
        EXPECT_LE(this_turn, 256u);
        ASSERT_LT(++turns, 5u);
    } while (!complete);
    EXPECT_EQ(600u, consumed);
    EXPECT_EQ(601u, raw.front());
}

TEST(RecordingBoundary, MessageTickWrapDoesNotPullFutureInputIntoEarlierBoundary) {
    EXPECT_TRUE(at_or_before(0xfffffffe, 1));
    EXPECT_TRUE(at_or_before(1, 1));
    EXPECT_FALSE(at_or_before(2, 1));
}

TEST(RecordingBoundary, GenericRawControlGetsSideFromExtendedFlagAndSidesDrainIndependently) {
    RAWKEYBOARD raw{};
    raw.VKey = VK_CONTROL;
    EXPECT_EQ(VK_LCONTROL, sided_key(raw));
    raw.Flags = RI_KEY_E0;
    EXPECT_EQ(VK_RCONTROL, sided_key(raw));
    Stream stream([](Packet) {});
    stream.key(VK_LCONTROL, false);
    stream.key(VK_RCONTROL, false);
    stream.key(VK_LCONTROL, true);
    EXPECT_EQ(RightControl, stream.held_keys());
    stream.key(VK_RCONTROL, true);
    EXPECT_EQ(0u, stream.held_keys());
}

TEST(RecordingBoundary, IdleInputIsNotRetainedAndOrdinaryZeroDelayEventIsNotABoundary) {
    std::vector<Packet> received;
    Stream stream([&](Packet packet) { received.push_back(std::move(packet)); });
    stream.input(key_event('A', false));
    EXPECT_TRUE(received.empty());
    stream.start(1);
    stream.input(key_event('A', false));
    ASSERT_EQ(2u, received.size());
    EXPECT_NE(nullptr, received[1].event);
    EXPECT_EQ(Boundary{}, received[1].boundary);
}
