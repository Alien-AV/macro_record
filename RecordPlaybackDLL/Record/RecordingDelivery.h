#pragma once
#include <condition_variable>
#include <deque>
#include <mutex>
#include "RecordingStream.h"

namespace record_playback { namespace capture {
// A queue entry may own an entire resolved prefix. Incoming native events join
// the unconsumed tail, spilling in bounded batches while the collector is busy.
// The producer never waits for event callbacks on a successful capture.
class DeliveryQueue {
public:
    static constexpr size_t capacity = 256;
    static constexpr size_t terminal_capacity = capacity + 1;
    explicit DeliveryQueue(PendingInput storage = PendingInput()) : storage_(std::move(storage)) {}
    void push(Packet packet) {
        std::unique_lock<std::mutex> lock(mutex_);
        if (packet.boundary == Boundary::Failed) {
            // The first failure has a reserved metadata slot even when normal
            // entries are full. Further rejected starts may wait only after their
            // source session has already failed; successful capture never waits.
            changed_.wait(lock, [&] { return closed_ || queue_.size() < terminal_capacity; });
        }
        if (closed_) throw PendingInputError();
        if (packet.event && storable(*packet.event) && !queue_.empty()) {
            auto& tail = queue_.back();
            if (tail.session == packet.session && !tail.omit_command
                && (tail.pending || (tail.event && storable(*tail.event)))) {
                if (!tail.pending) {
                    tail.pending = std::make_unique<PendingInput>(storage_.fresh());
                    tail.pending->append(std::move(tail.event), false);
                }
                tail.pending->append(std::move(packet.event), false);
                return;
            }
        }
        if (packet.boundary != Boundary::Failed && queue_.size() >= capacity) throw PendingInputError();
        queue_.push_back(std::move(packet));
        lock.unlock();
        changed_.notify_all();
    }
    bool take(Packet& packet, bool wait) {
        std::unique_lock<std::mutex> lock(mutex_);
        if (wait) changed_.wait(lock, [&] { return closed_ || !queue_.empty(); });
        if (queue_.empty()) return false;
        packet = std::move(queue_.front());
        queue_.pop_front();
        lock.unlock();
        changed_.notify_all();
        return true;
    }
    void close() {
        { std::lock_guard<std::mutex> lock(mutex_); closed_ = true; }
        changed_.notify_all();
    }
    size_t size() const { std::lock_guard<std::mutex> lock(mutex_); return queue_.size(); }
private:
    static bool storable(const Event& event) {
        // Unknown/derived payloads retain their original owning object.
        return typeid(event) == typeid(KeyboardEvent) || typeid(event) == typeid(MouseEvent);
    }
    PendingInput storage_;
    mutable std::mutex mutex_;
    std::condition_variable changed_;
    std::deque<Packet> queue_;
    bool closed_ = false;
};

// Collector-thread state. All events and terminal notifications use the same
// FIFO, including a failure discovered only when reading a transferred spool.
class DeliveryCollector {
public:
    using Failure = std::function<void(uint64_t)>;
    explicit DeliveryCollector(Stream::Sink sink, Failure failure = [](uint64_t) {})
        : sink_(std::move(sink)), failure_(std::move(failure)) {}
    void input(Packet packet) {
        if (failed_session_ && packet.session == failed_session_) return;
        if (packet.boundary == Boundary::Started) {
            active_session_ = packet.session;
            omitted_ = {};
            sink_(std::move(packet));
            return;
        }
        if (packet.boundary == Boundary::Failed) {
            failed_session_ = packet.session;
            if (active_session_ == packet.session) active_session_ = 0;
            sink_(std::move(packet));
            return;
        }
        if (!active_session_ || packet.session != active_session_) return;
        const auto session = packet.session;
        try {
            if (packet.pending) {
                const auto tail = packet.pending->drain(packet.omit_command, [&](std::unique_ptr<Event> event) {
                    deliver(session, std::move(event));
                });
                add_delay(omitted_, tail);
            } else if (packet.event) deliver(session, std::move(packet.event));
            else {
                if (packet.boundary == Boundary::Stopped) active_session_ = 0;
                sink_(std::move(packet));
            }
        } catch (const PendingInputError&) {
            // Close before notifying either thread. Queued input/end for this
            // failed session must not turn a partial capture into a success.
            packet.pending.reset();
            failed_session_ = session;
            active_session_ = 0;
            failure_(session);
            sink_({session, nullptr, Boundary::Failed});
        }
    }
private:
    static void add_delay(std::chrono::microseconds& target, std::chrono::microseconds value) {
        if (value.count() < 0 || value.count() > (std::numeric_limits<int64_t>::max)() - target.count()) throw PendingInputError();
        target += value;
    }
    void deliver(uint64_t session, std::unique_ptr<Event> event) {
        if (const auto marker = dynamic_cast<MarkerEvent*>(event.get())) {
            auto resolved = marker->marker->await();
            if (!resolved.event) {
                add_delay(omitted_, event->time_since_last_event);
                sink_({session, nullptr, static_cast<Boundary>(resolved.result)});
                return;
            }
            resolved.event->time_since_last_event = event->time_since_last_event;
            event = std::move(resolved.event);
        }
        add_delay(event->time_since_last_event, omitted_);
        omitted_ = {};
        sink_({session, std::move(event)});
    }
    std::chrono::microseconds omitted_{};
    Stream::Sink sink_;
    Failure failure_;
    uint64_t failed_session_ = 0;
    uint64_t active_session_ = 0;
};
}}
