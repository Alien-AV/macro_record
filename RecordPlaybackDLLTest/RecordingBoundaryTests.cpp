#include "pch.h"
#include "RecordingTestSink.h"
#include "../RecordPlaybackDLL/Record/RecordingStream.h"
#include "../RecordPlaybackDLL/Common/KeyboardEvent.h"
#include "../Common/protobuf/cpp/Events.pb.h"
#include <deque>

using namespace record_playback::capture;

TEST(RecordingBoundary, LegacyProtobufReaderRejectsVersionedMacroEnvelope) {
    const std::string envelope("\0MACRO2\n{\"Version\":2}", 21);
    protobufGenerated::ProtobufInputEventList wire;
    EXPECT_FALSE(wire.ParseFromString(envelope));
}

TEST(RecordingBoundary, OriginsAreMetadataOnEachOrderedBoundaryNeverSyntheticActions) {
    std::vector<Packet> packets;
    Stream stream(collect_packets(packets));
    stream.key(VK_LCONTROL, false);
    stream.key('Q', false);
    ASSERT_TRUE(stream.start(1, NoStopGesture, {-500, -100, true}));
    stream.stop(1);
    ASSERT_TRUE(stream.start(2, NoStopGesture, {900, 200, true}));
    stream.stop(2);
    ASSERT_EQ(4u, packets.size());
    EXPECT_EQ(1u, packets[0].session);
    EXPECT_EQ(Q | LeftControl, packets[0].held_keys);
    EXPECT_TRUE(packets[0].origin.valid);
    EXPECT_EQ(-500, packets[0].origin.x);
    EXPECT_EQ(-100, packets[0].origin.y);
    EXPECT_EQ(2u, packets[2].session);
    EXPECT_EQ(900, packets[2].origin.x);
    for (const auto& packet : packets) EXPECT_EQ(nullptr, packet.event);
}

TEST(RecordingBoundary, MissingOriginIsExplicitAndCannotLeakFromEarlierSession) {
    std::vector<Packet> packets;
    Stream stream(collect_packets(packets));
    ASSERT_TRUE(stream.start(1, NoStopGesture, {44, 55, true})); stream.stop(1);
    ASSERT_TRUE(stream.start(2));
    EXPECT_FALSE(packets.back().origin.valid);
    EXPECT_EQ(0, packets.back().origin.x);
}

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
    Stream stream(collect_packets(received));
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
    Stream stream(collect_packets(received));
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
    Stream stream(collect_packets(received));
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
    Stream stream(collect_packets(received));
    stream.input(key_event('A', false));
    EXPECT_TRUE(received.empty());
    stream.start(1);
    stream.input(key_event('A', false));
    ASSERT_EQ(2u, received.size());
    EXPECT_NE(nullptr, received[1].event);
    EXPECT_EQ(Boundary{}, received[1].boundary);
}

TEST(RecordingBoundary, IdleReleasesSurviveRepressBeforeRolloverAndAreScopedToThatGap) {
    std::vector<Packet> received;
    Stream stream(collect_packets(received));
    stream.key('Q', false);
    stream.key(VK_LCONTROL, false);
    stream.key(VK_RCONTROL, false);
    stream.start(1);
    stream.stop(1);
    stream.key('Q', true);
    stream.key(VK_LCONTROL, true);
    stream.key('Q', false);
    stream.key(VK_LCONTROL, false);
    stream.start(2);
    ASSERT_EQ(3u, received.size());
    EXPECT_EQ(Q | LeftControl | RightControl, received[2].held_keys);
    EXPECT_EQ(Q | LeftControl, received[2].idle_released_keys);
    // There was no idle RightControl release, despite the other two keys cycling.
    stream.stop(2);
    stream.start(3);
    ASSERT_EQ(5u, received.size());
    EXPECT_EQ(0u, received[4].idle_released_keys);
    EXPECT_EQ(Q | LeftControl | RightControl, received[4].held_keys);
}

TEST(RecordingBoundary, IdleReleaseWithoutRepressIsReportedAlongsideEmptyHeldState) {
    std::vector<Packet> received;
    Stream stream(collect_packets(received));
    stream.key('Q', false);
    stream.key(VK_RCONTROL, false);
    stream.start(1);
    stream.stop(1);
    stream.key('Q', true);
    stream.key(VK_RCONTROL, true);
    stream.start(2);
    ASSERT_EQ(3u, received.size());
    EXPECT_EQ(0u, received[2].held_keys);
    EXPECT_EQ(Q | RightControl, received[2].idle_released_keys);
}
