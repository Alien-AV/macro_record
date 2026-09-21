#include "pch.h"
#include "RecordingTestSink.h"
#include "../RecordPlaybackDLL/Record/RecordingStream.h"
#include <limits>

using namespace record_playback::capture;

namespace {
struct StorageStats {
    std::vector<std::wstring> paths;
    size_t opened = 0, closed = 0, writes = 0, bytes = 0;
    bool fail_create = false, fail_write = false, fail_rewind = false, fail_read = false;
};
class ObservedStorage final : public PendingStorage {
public:
    explicit ObservedStorage(StorageStats& stats) : stats_(stats) {
        stats_.paths.push_back(file_.path());
        ++stats_.opened;
    }
    ~ObservedStorage() override { ++stats_.closed; }
    void write(const void* data, DWORD bytes) override {
        if (stats_.fail_write) throw PendingInputError();
        file_.write(data, bytes);
        ++stats_.writes;
        stats_.bytes += bytes;
    }
    void rewind() override {
        if (stats_.fail_rewind) throw PendingInputError();
        file_.rewind();
    }
    void read(void* data, DWORD bytes) override {
        if (stats_.fail_read) throw PendingInputError();
        file_.read(data, bytes);
    }
private:
    StorageStats& stats_;
    TemporaryPendingStorage file_;
};
PendingInput::Factory storage_factory(StorageStats& stats) {
    return [&stats]() -> std::unique_ptr<PendingStorage> {
        if (stats.fail_create) throw PendingInputError();
        return std::make_unique<ObservedStorage>(stats);
    };
}
void ctrl(Stream& stream, int64_t delay = 7, bool up = false) {
    auto event = std::make_unique<KeyboardEvent>();
    event->virtualKeyCode = VK_CONTROL;
    event->keyUp = up;
    event->time_since_last_event = std::chrono::microseconds(delay);
    stream.key(VK_CONTROL, up);
    stream.input(std::move(event), 1);
}
void move(Stream& stream, int64_t delay = 11) {
    auto event = std::make_unique<MouseEvent>(1, -2, MOUSEEVENTF_MOVE, 0, false, true);
    event->time_since_last_event = std::chrono::microseconds(delay);
    stream.input(std::move(event), 1);
}
void deleted(const StorageStats& stats) {
    EXPECT_EQ(stats.opened, stats.closed);
    for (const auto& path : stats.paths) {
        EXPECT_EQ(INVALID_FILE_ATTRIBUTES, GetFileAttributesW(path.c_str()));
        EXPECT_EQ(ERROR_FILE_NOT_FOUND, GetLastError());
    }
}
}

TEST(RecordingStorage, BatchedUniquePrivateFilesAreDeletedOnConfirmationCancellationAndDestruction) {
    for (int resolution = 0; resolution < 4; ++resolution) {
        StorageStats stats;
        std::vector<Packet> received;
        {
            Stream stream(collect_packets(received), PendingInput(storage_factory(stats)));
            stream.start(1, ControlW);
            ctrl(stream);
            for (size_t i = 1; i < PendingInput::capacity; ++i) move(stream);
            EXPECT_EQ(0u, stats.opened);
            for (int i = 0; i < 1025; ++i) move(stream);
            ASSERT_EQ(1u, stats.opened);
            EXPECT_EQ(5u, stats.writes); // 1,281 events, five full disk batches.
            EXPECT_LT(stats.bytes, PendingInput::max_bytes);
            EXPECT_NE(INVALID_FILE_ATTRIBUTES, GetFileAttributesW(stats.paths[0].c_str()));
            const auto outsider = CreateFileW(stats.paths[0].c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                nullptr, OPEN_EXISTING, 0, nullptr);
            EXPECT_EQ(INVALID_HANDLE_VALUE, outsider); // No reader can open raw input while owned.
            if (outsider != INVALID_HANDLE_VALUE) CloseHandle(outsider);
            if (resolution == 0) stream.stop(1, ControlW, 1);
            if (resolution == 1) stream.stop(1);
            if (resolution == 2) ctrl(stream, 13, true); // Genuine tap cancels candidate.
            if (resolution < 3) deleted(stats);
        }
        deleted(stats); // Abort/disposal closes the outstanding candidate too.
        if (resolution == 3) EXPECT_EQ(1u, received.size());
    }
    StorageStats first, second;
    {
        Stream a([](Packet) {}, PendingInput(storage_factory(first)));
        Stream b([](Packet) {}, PendingInput(storage_factory(second)));
        a.start(1, ControlW); b.start(2, ControlW);
        ctrl(a); ctrl(b);
        for (int i = 0; i < 300; ++i) { move(a); move(b); }
        ASSERT_EQ(1u, first.paths.size());
        ASSERT_EQ(1u, second.paths.size());
        EXPECT_NE(first.paths[0], second.paths[0]);
    }
    deleted(first); deleted(second);
}

TEST(RecordingStorage, CreateWriteSeekReadFailuresEndCaptureExplicitlyAndCloseBeforeRestart) {
    for (int failure = 0; failure < 4; ++failure) {
        StorageStats stats;
        std::vector<Packet> received;
        Stream stream(collect_packets(received), PendingInput(storage_factory(stats)));
        stream.start(1, ControlW);
        ctrl(stream);
        stats.fail_create = failure == 0;
        for (int i = 0; i < 300; ++i) move(stream);
        if (failure == 1) {
            stats.fail_write = true;
            for (int i = 0; i < 300; ++i) move(stream);
        }
        if (failure >= 2) {
            stats.fail_rewind = failure == 2;
            stats.fail_read = failure == 3;
        }
        stream.stop(1, ControlW, 1);
        ASSERT_EQ(2u, received.size());
        EXPECT_EQ(Boundary::Failed, received.back().boundary);
        EXPECT_EQ(0u, stream.session());
        deleted(stats);
        stats.fail_create = stats.fail_write = stats.fail_rewind = stats.fail_read = false;
        ctrl(stream, 0, true);
        stream.start(2, ControlW);
        ctrl(stream);
        for (int i = 0; i < 300; ++i) move(stream);
        stream.stop(1, ControlW, 1); // Failed session cannot resolve the new one.
        EXPECT_EQ(2u, stream.session());
        stream.stop(2, ControlW, 1);
        EXPECT_EQ(Boundary::Stopped, received.back().boundary);
        EXPECT_EQ(2u, received.back().session);
        deleted(stats);
        if (stats.paths.size() == 2) EXPECT_NE(stats.paths[0], stats.paths[1]);
    }
}

TEST(RecordingStorage, ExplicitByteBudgetFailsWithoutPublishingCandidateOrRetainingSpool) {
    StorageStats stats;
    std::vector<Packet> received;
    constexpr size_t limit = PendingInput::capacity * 2 + 1;
    Stream stream(collect_packets(received), PendingInput(storage_factory(stats), limit));
    stream.start(1, ControlW);
    ctrl(stream);
    for (size_t i = 1; i < limit; ++i) move(stream);
    EXPECT_EQ(1u, received.size());
    EXPECT_EQ(2u, stats.writes);
    const auto disk_bytes = stats.bytes;
    move(stream);
    EXPECT_EQ(disk_bytes, stats.bytes);
    ASSERT_EQ(2u, received.size());
    EXPECT_EQ(Boundary::Failed, received.back().boundary);
    stream.stop(1);
    EXPECT_EQ(2u, received.size());
    deleted(stats);
}

TEST(RecordingStorage, DelayCarryChecksOverflowInMemoryAndAfterSpillWithoutWrappingOrSuccess) {
    for (bool spill : {false, true}) for (bool overflow : {false, true}) {
        StorageStats stats;
        std::vector<Packet> received;
        Stream stream(collect_packets(received), PendingInput(storage_factory(stats)));
        stream.start(1, ControlW);
        ctrl(stream, (std::numeric_limits<int64_t>::max)() - 1);
        ctrl(stream, 1);
        if (spill) for (size_t i = 0; i < PendingInput::capacity * 2; ++i) ctrl(stream, 0);
        move(stream, overflow ? 1 : 0);
        stream.stop(1, ControlW, 1);
        if (overflow) {
            ASSERT_EQ(2u, received.size());
            EXPECT_EQ(Boundary::Failed, received.back().boundary);
        } else {
            ASSERT_EQ(3u, received.size());
            EXPECT_EQ((std::numeric_limits<int64_t>::max)(), received[1].event->time_since_last_event.count());
            EXPECT_EQ(Boundary::Stopped, received.back().boundary);
        }
        deleted(stats);
    }
}

TEST(RecordingStorage, RemovedModifierDelaySumItselfCannotOverflow) {
    std::vector<Packet> received;
    Stream stream(collect_packets(received));
    stream.start(1, ControlW);
    ctrl(stream, (std::numeric_limits<int64_t>::max)());
    ctrl(stream, 1);
    stream.stop(1, ControlW, 1);
    ASSERT_EQ(2u, received.size());
    EXPECT_EQ(Boundary::Failed, received.back().boundary);
}

TEST(RecordingStorage, QuarterMillionSamplesStayMemoryBoundedAndReplayWithoutCoalescingOrDelayLoss) {
    StorageStats stats;
    size_t received = 0;
    int64_t duration = 0;
    StopChord filter(collect_events([&](std::unique_ptr<Event> event) {
        const auto mouse = dynamic_cast<MouseEvent*>(event.get());
        ASSERT_NE(nullptr, mouse);
        EXPECT_EQ(static_cast<LONG>(received), mouse->x);
        EXPECT_EQ(-static_cast<LONG>(received), mouse->y);
        EXPECT_EQ(MOUSEEVENTF_MOVE, mouse->ActionType);
        EXPECT_EQ(std::chrono::microseconds(received ? 11 : 18), mouse->time_since_last_event);
        duration += mouse->time_since_last_event.count();
        ++received;
    }), PendingInput(storage_factory(stats)));
    filter.start(ControlW, {});
    auto control = std::make_unique<KeyboardEvent>();
    control->virtualKeyCode = VK_LCONTROL;
    control->time_since_last_event = std::chrono::microseconds(7);
    filter.input(std::move(control), 1);
    constexpr size_t samples = 262145;
    for (size_t i = 0; i < samples; ++i) {
        auto mouse = std::make_unique<MouseEvent>(static_cast<LONG>(i), -static_cast<LONG>(i), MOUSEEVENTF_MOVE, 0, false, true);
        mouse->time_since_last_event = std::chrono::microseconds(11);
        filter.input(std::move(mouse), 1);
        ASSERT_LE(filter.pending_count(), PendingInput::capacity);
    }
    EXPECT_EQ(0u, received);
    EXPECT_EQ(1024u, stats.writes);
    EXPECT_LT(stats.bytes, PendingInput::max_bytes);
    filter.finish(ControlW, 1);
    EXPECT_EQ(samples, received);
    EXPECT_EQ(7 + 11 * samples, duration);
    EXPECT_EQ(0u, filter.pending_count());
    deleted(stats);
}
