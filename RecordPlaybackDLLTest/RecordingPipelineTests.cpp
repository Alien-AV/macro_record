#include "pch.h"
#include "../RecordPlaybackDLL/Record/RecordingPipeline.h"
#include <future>
#include <limits>
#include <thread>

using namespace record_playback::capture;
using Microseconds = std::chrono::microseconds;

namespace {
struct StorageProbe {
    std::vector<std::wstring> paths;
    std::atomic<size_t> writes{0}, reads{0}, opened{0}, closed{0}, live_bytes{0};
    std::atomic<bool> fail_read{false};
    std::function<void()> on_read = [] {};
};
class ProbedStorage final : public PendingStorage {
public:
    explicit ProbedStorage(StorageProbe& probe) : probe_(probe) {
        probe_.paths.push_back(file_.path());
        ++probe_.opened;
    }
    ~ProbedStorage() override { ++probe_.closed; probe_.live_bytes -= bytes_; }
    void write(const void* data, DWORD bytes) override {
        file_.write(data, bytes);
        ++probe_.writes;
        bytes_ += bytes;
        probe_.live_bytes += bytes;
    }
    void rewind() override { file_.rewind(); }
    void read(void* data, DWORD bytes) override {
        probe_.on_read();
        ++probe_.reads;
        if (probe_.fail_read) throw PendingInputError();
        file_.read(data, bytes);
    }
private:
    StorageProbe& probe_;
    TemporaryPendingStorage file_;
    size_t bytes_ = 0;
};
PendingInput pending(StorageProbe& probe, std::shared_ptr<PendingBudget> budget = std::make_shared<PendingBudget>()) {
    return PendingInput([&probe] { return std::make_unique<ProbedStorage>(probe); },
        PendingInput::max_bytes, std::move(budget));
}
void closed(const StorageProbe& probe) {
    EXPECT_EQ(probe.opened.load(), probe.closed.load());
    EXPECT_EQ(0u, probe.live_bytes.load());
    for (const auto& path : probe.paths) EXPECT_EQ(INVALID_FILE_ATTRIBUTES, GetFileAttributesW(path.c_str()));
}
RAWKEYBOARD raw_key(WORD key, bool up = false) {
    RAWKEYBOARD raw{};
    raw.VKey = key;
    raw.Flags = up ? RI_KEY_BREAK : 0;
    return raw;
}
RAWMOUSE raw_motion(LONG coordinate) {
    RAWMOUSE raw{};
    raw.lLastX = coordinate;
    raw.lLastY = -coordinate;
    return raw;
}
std::unique_ptr<KeyboardEvent> key_event(int64_t delay) {
    auto event = std::make_unique<KeyboardEvent>();
    event->virtualKeyCode = VK_CONTROL;
    event->time_since_last_event = Microseconds(delay);
    return event;
}
std::unique_ptr<MouseEvent> mouse_event(int64_t delay = 1) {
    auto event = std::make_unique<MouseEvent>(1, -2, MOUSEEVENTF_MOVE, 0, false, true);
    event->time_since_last_event = Microseconds(delay);
    return event;
}
}

TEST(RecordingPipeline, SlowCallbacksCannotDelayRawSourceWhenLongPrefixBecomesGenuine) {
    for (bool release : {false, true}) {
        StorageProbe probe;
        auto budget = std::make_shared<PendingBudget>();
        int64_t now = 0;
        bool in_source = false;
        size_t callbacks = 0;
        std::vector<Packet> received;
        std::function<void(int64_t)> advance;
        // 8 kHz source; a callback takes 75 us, fast enough for normal live
        // input but costly when thousands of provisional events resolve at once.
        Pipeline pipeline([&](Packet packet) {
            EXPECT_FALSE(in_source) << "Callbacks must never execute inside raw capture";
            const bool event = packet.event != nullptr;
            received.push_back(std::move(packet));
            if (event) {
                ++callbacks;
                if (!in_source) advance(now + 75);
            }
        }, [](uint64_t) { ADD_FAILURE() << "No capture failure expected"; },
            [&] { return std::chrono::steady_clock::time_point(Microseconds(now)); }, pending(probe, budget));
        probe.on_read = [&] { EXPECT_FALSE(in_source) << "Source must transfer, not read, the resolved spool"; };
        ASSERT_TRUE(pipeline.start(1, ControlW));
        ASSERT_TRUE(pipeline.collect_one()); // Started precedes every input.
        constexpr int samples = 16385;
        constexpr int stop_index = 2 * samples + 3;
        int next = 1;
        const auto dispatch = [&](int index) {
            now = int64_t(index) * 125;
            const auto before = callbacks;
            const auto reads = probe.reads.load();
            in_source = true;
            if (index == 1) pipeline.keyboard(raw_key(VK_CONTROL), DWORD(now / 1000));
            else if (index == samples + 2)
                pipeline.keyboard(raw_key(release ? VK_CONTROL : 'C', release), DWORD(now / 1000));
            else if (index == stop_index) pipeline.stop(1);
            else pipeline.mouse(raw_motion(index), {}, DWORD(now / 1000));
            in_source = false;
            EXPECT_EQ(before, callbacks);
            EXPECT_EQ(reads, probe.reads.load());
            EXPECT_EQ(int64_t(index) * 125, now) << "Callback drain time leaked into source servicing";
            EXPECT_LE(pipeline.queued_batches(), DeliveryQueue::capacity);
            EXPECT_LE(budget->used(), PendingBudget::max_bytes);
        };
        advance = [&](int64_t target) {
            while (next <= stop_index && int64_t(next) * 125 <= target) dispatch(next++);
            now = target;
        };
        advance(int64_t(samples + 2) * 125); // Ctrl release/typing resolves the entire prefix.
        EXPECT_EQ(0u, callbacks);
        EXPECT_EQ(0u, probe.reads.load());
        ASSERT_EQ(1u, pipeline.queued_batches());
        ASSERT_TRUE(pipeline.collect_one()); // Virtual source keeps running during every callback.
        EXPECT_GT(next, samples + 2 + 256);
        EXPECT_GT(probe.opened.load(), 1u); // New raw input needed its own bounded delivery spool.
        while (next <= stop_index || pipeline.queued_batches()) {
            if (!pipeline.collect_one()) advance(int64_t(next) * 125);
        }
        pipeline.close();
        ASSERT_EQ(size_t(stop_index + 1), received.size());
        EXPECT_EQ(Boundary::Started, received.front().boundary);
        EXPECT_EQ(Boundary::Stopped, received.back().boundary);
        int64_t total = 0;
        for (int index = 1; index < stop_index; ++index) {
            const auto& packet = received[index];
            ASSERT_NE(nullptr, packet.event);
            EXPECT_EQ(1u, packet.session);
            EXPECT_EQ(Microseconds(125), packet.event->time_since_last_event);
            total += packet.event->time_since_last_event.count();
            if (index == 1 || index == samples + 2) {
                const auto key = dynamic_cast<KeyboardEvent*>(packet.event.get());
                ASSERT_NE(nullptr, key);
                EXPECT_EQ(index == 1 || release ? VK_LCONTROL : 'C', key->virtualKeyCode);
                EXPECT_EQ(index != 1 && release, key->keyUp);
            } else {
                const auto mouse = dynamic_cast<MouseEvent*>(packet.event.get());
                ASSERT_NE(nullptr, mouse);
                EXPECT_EQ(index, mouse->x);
                EXPECT_EQ(-index, mouse->y);
                EXPECT_TRUE(mouse->relative_position);
                EXPECT_EQ(MOUSEEVENTF_MOVE, mouse->ActionType);
            }
        }
        EXPECT_EQ(int64_t(stop_index - 1) * 125, total);
        EXPECT_EQ(0u, budget->used());
        closed(probe);
    }
}

TEST(RecordingPipeline, PausedCollectorDoesNotHoldQueueLockOrBackpressureContinuedCapture) {
    StorageProbe probe;
    std::atomic<int64_t> now{0};
    std::promise<void> entered, resume;
    auto resumed = resume.get_future().share();
    bool first = true;
    std::vector<Packet> received;
    Pipeline pipeline([&](Packet packet) {
        if (packet.event && first) {
            first = false;
            entered.set_value();
            resumed.wait();
        }
        received.push_back(std::move(packet));
    }, [](uint64_t) { ADD_FAILURE() << "No capture failure expected"; },
        [&] { return std::chrono::steady_clock::time_point(Microseconds(now.load())); }, pending(probe));
    pipeline.start(1, ControlW);
    pipeline.collect_one();
    now += 125;
    pipeline.keyboard(raw_key(VK_CONTROL), 1);
    for (int i = 0; i < 1025; ++i) {
        now += 125;
        pipeline.mouse(raw_motion(i + 1), {}, 1);
    }
    now += 125;
    pipeline.keyboard(raw_key(VK_CONTROL, true), 1);
    auto collector = std::async(std::launch::async, [&] { return pipeline.collect_one(); });
    const auto entered_status = entered.get_future().wait_for(std::chrono::seconds(2));
    if (entered_status != std::future_status::ready) {
        resume.set_value();
        collector.get();
        FAIL() << "Collector did not reach its callback";
    }
    auto producer = std::async(std::launch::async, [&] {
        for (int i = 0; i < 8193; ++i) {
            now += 125;
            pipeline.mouse(raw_motion(i + 2000), {}, 1);
        }
        pipeline.stop(1);
    });
    const auto producer_status = producer.wait_for(std::chrono::seconds(2));
    resume.set_value(); // Always unblock cleanup, including a failed regression.
    EXPECT_EQ(std::future_status::ready, producer_status);
    producer.get();
    EXPECT_TRUE(collector.get());
    pipeline.close();
    while (pipeline.collect_one(true)) {}
    ASSERT_EQ(1025u + 8193u + 4u, received.size());
    EXPECT_EQ(Boundary::Started, received.front().boundary);
    EXPECT_EQ(Boundary::Stopped, received.back().boundary);
    for (size_t i = 1; i + 1 < received.size(); ++i)
        EXPECT_EQ(Microseconds(125), received[i].event->time_since_last_event);
    closed(probe);
}

TEST(RecordingPipeline, ConfirmedBatchAndImmediateRolloverKeepBoundariesOriginsAndClockSeparate) {
    int64_t now = 0;
    std::vector<Packet> received;
    Pipeline pipeline([&](Packet packet) { received.push_back(std::move(packet)); },
        [](uint64_t) { ADD_FAILURE(); }, [&] { return std::chrono::steady_clock::time_point(Microseconds(now)); });
    pipeline.start(1, ControlW, {-100, 200, true});
    now = 7; pipeline.keyboard(raw_key(VK_CONTROL), 1);
    now = 18; pipeline.mouse(raw_motion(10), {}, 2);
    now = 31; pipeline.keyboard(raw_key('W'), 3);
    pipeline.stop(1, ControlW, 3);
    now = 40; pipeline.start(2, NoStopGesture, {400, -300, true});
    now = 87; pipeline.mouse(raw_motion(20), {}, 4);
    pipeline.stop(2);
    EXPECT_TRUE(received.empty());
    pipeline.close();
    while (pipeline.collect_one(true)) {}
    ASSERT_EQ(6u, received.size());
    EXPECT_EQ(Boundary::Started, received[0].boundary);
    EXPECT_EQ(-100, received[0].origin.x);
    EXPECT_EQ(1u, received[1].session);
    EXPECT_EQ(Microseconds(18), received[1].event->time_since_last_event);
    EXPECT_EQ(Boundary::Stopped, received[2].boundary);
    EXPECT_EQ(Boundary::Started, received[3].boundary);
    EXPECT_EQ(400, received[3].origin.x);
    EXPECT_EQ(2u, received[4].session);
    EXPECT_EQ(Microseconds(47), received[4].event->time_since_last_event);
    EXPECT_EQ(Boundary::Stopped, received[5].boundary);
}

TEST(RecordingPipeline, CollectorReadAndDelayOverflowFailuresOwnTheirSessionAndCannotMutateNewerSource) {
    for (bool overflow : {false, true}) {
        StorageProbe probe;
        auto budget = std::make_shared<PendingBudget>();
        auto storage = pending(probe, budget);
        DeliveryQueue queue(storage.fresh());
        Stream stream([&](Packet packet) { queue.push(std::move(packet)); }, std::move(storage));
        std::vector<Packet> received;
        std::vector<uint64_t> failure_messages;
        DeliveryCollector collector([&](Packet packet) { received.push_back(std::move(packet)); },
            [&](uint64_t session) { failure_messages.push_back(session); }); // Post control only; never touch Stream here.
        stream.start(1, ControlW);
        stream.key(VK_CONTROL, false);
        stream.input(key_event(overflow ? (std::numeric_limits<int64_t>::max)() : 7), 1);
        stream.input(key_event(overflow ? 1 : 0), 1);
        for (int i = 0; i < 513; ++i) stream.input(mouse_event(0), 1);
        stream.stop(1, ControlW, 1);
        // Even data/success queued behind the failed batch must be suppressed.
        queue.push({1, mouse_event(99)});
        queue.push({1, nullptr, Boundary::Stopped});
        stream.key(VK_CONTROL, true);
        stream.start(2);
        stream.input(mouse_event(47), 1);
        probe.fail_read = !overflow;
        Packet packet{};
        ASSERT_TRUE(queue.take(packet, false)); collector.input(std::move(packet));
        ASSERT_TRUE(queue.take(packet, false)); collector.input(std::move(packet));
        ASSERT_EQ((std::vector<uint64_t>{1}), failure_messages);
        EXPECT_EQ(2u, stream.session());
        // Delivery of the posted error is explicitly a source-thread operation.
        stream.fail(failure_messages.front());
        EXPECT_EQ(2u, stream.session());
        stream.stop(2);
        queue.push({1, mouse_event(101)}); // Old data/success even after a newer Started.
        queue.push({1, nullptr, Boundary::Stopped});
        queue.close();
        while (queue.take(packet, true)) collector.input(std::move(packet));
        ASSERT_EQ(5u, received.size());
        EXPECT_EQ(Boundary::Started, received[0].boundary);
        EXPECT_EQ(1u, received[1].session);
        EXPECT_EQ(Boundary::Failed, received[1].boundary);
        EXPECT_EQ(2u, received[2].session);
        EXPECT_EQ(Boundary::Started, received[2].boundary);
        EXPECT_EQ(Microseconds(47), received[3].event->time_since_last_event);
        EXPECT_EQ(2u, received[4].session);
        EXPECT_EQ(Boundary::Stopped, received[4].boundary);
        EXPECT_EQ(0u, budget->used());
        closed(probe);
    }
}

TEST(RecordingPipeline, SharedBudgetExhaustionStillDeliversFailureAndAllowsCleanRestart) {
    StorageProbe probe;
    constexpr size_t limit = 32 * 1024;
    auto budget = std::make_shared<PendingBudget>(limit);
    int64_t now = 0;
    std::vector<Packet> received;
    Pipeline pipeline([&](Packet packet) { received.push_back(std::move(packet)); }, [](uint64_t) { ADD_FAILURE(); },
        [&] { return std::chrono::steady_clock::time_point(Microseconds(now)); }, pending(probe, budget));
    pipeline.start(1, ControlW);
    // Multiple transferred prefixes and the active prefix share one bound.
    for (int round = 0; round < 3 && pipeline.session(); ++round) {
        now += 125; pipeline.keyboard(raw_key(VK_CONTROL), 1);
        for (int i = 0; i < 400 && pipeline.session(); ++i) {
            now += 125; pipeline.mouse(raw_motion(i + 1), {}, 1);
            EXPECT_LE(budget->used(), limit);
            EXPECT_LE(probe.live_bytes.load(), limit);
        }
        now += 125; pipeline.keyboard(raw_key(VK_CONTROL, true), 1);
    }
    ASSERT_EQ(0u, pipeline.session());
    EXPECT_GT(probe.opened.load(), 1u);
    EXPECT_TRUE(received.empty());
    pipeline.stop(1);
    while (pipeline.collect_one()) {}
    ASSERT_FALSE(received.empty());
    EXPECT_EQ(Boundary::Started, received.front().boundary);
    EXPECT_EQ(Boundary::Failed, received.back().boundary);
    for (const auto& packet : received) EXPECT_NE(Boundary::Stopped, packet.boundary);
    EXPECT_EQ(0u, budget->used());
    closed(probe);
    const auto previous = received.size();
    pipeline.start(2);
    now += 47; pipeline.mouse(raw_motion(123), {}, 2);
    pipeline.stop(2);
    pipeline.close();
    while (pipeline.collect_one(true)) {}
    ASSERT_EQ(previous + 3, received.size());
    EXPECT_EQ(Boundary::Started, received[previous].boundary);
    EXPECT_EQ(Microseconds(47), received[previous + 1].event->time_since_last_event);
    EXPECT_EQ(Boundary::Stopped, received.back().boundary);
}

TEST(RecordingPipeline, ShutdownAndAbandonmentReleaseTransferredQueuedAndProvisionalStorage) {
    for (bool drain : {false, true}) {
        StorageProbe probe;
        auto budget = std::make_shared<PendingBudget>();
        {
            int64_t now = 0;
            Pipeline pipeline([](Packet) {}, [](uint64_t) { ADD_FAILURE(); },
                [&] { return std::chrono::steady_clock::time_point(Microseconds(now)); }, pending(probe, budget));
            pipeline.start(1, ControlW);
            now += 1; pipeline.keyboard(raw_key(VK_CONTROL), 1);
            for (int i = 0; i < 513; ++i) { now += 1; pipeline.mouse(raw_motion(i + 1), {}, 1); }
            now += 1; pipeline.keyboard(raw_key(VK_CONTROL, true), 1);
            now += 1; pipeline.keyboard(raw_key(VK_CONTROL), 1);
            for (int i = 0; i < 513; ++i) { now += 1; pipeline.mouse(raw_motion(i + 1), {}, 1); }
            ASSERT_EQ(2u, probe.opened.load());
            EXPECT_GT(budget->used(), 0u);
            if (drain) {
                pipeline.stop(1);
                pipeline.close();
                while (pipeline.collect_one(true)) {}
                EXPECT_EQ(0u, budget->used());
                closed(probe);
            }
        }
        EXPECT_EQ(0u, budget->used());
        closed(probe);
    }
}

TEST(RecordingPipeline, TerminalFailureHasReservedMetadataCapacityWithoutAnyDataBudget) {
    StorageProbe probe;
    auto budget = std::make_shared<PendingBudget>(0);
    DeliveryQueue queue(pending(probe, budget));
    for (size_t i = 0; i < DeliveryQueue::capacity; ++i) queue.push({i + 1, nullptr, Boundary::Started});
    EXPECT_THROW(queue.push({999, mouse_event()}), PendingInputError);
    queue.push({DeliveryQueue::capacity, nullptr, Boundary::Failed});
    EXPECT_EQ(DeliveryQueue::terminal_capacity, queue.size());
    EXPECT_EQ(0u, budget->used());
    queue.close();
    Packet packet{};
    size_t count = 0;
    while (queue.take(packet, true)) {
        ++count;
        if (count == DeliveryQueue::terminal_capacity) {
            EXPECT_EQ(DeliveryQueue::capacity, packet.session);
            EXPECT_EQ(Boundary::Failed, packet.boundary);
        }
    }
    EXPECT_EQ(DeliveryQueue::terminal_capacity, count);
    closed(probe);
}
