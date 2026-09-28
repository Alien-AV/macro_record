#pragma once
#include <condition_variable>
#include <mutex>
#include "../Common/WaitEvent.h"

namespace record_playback { namespace capture {
struct CaptureGesture {
    uint32_t modifiers = 0, key = 0;
    bool operator==(const CaptureGesture& other) const { return modifiers == other.modifiers && key == other.key; }
};
enum class CaptureResult : uint32_t { Inserted = 0, HeldInput = 4, Cancelled = 5, Stale = 6, TimedOut = 7 };

// A raw-source reservation. Only its payload crosses threads; its position and
// timing are fixed before any UI callback can supply a sampled condition.
class WaitMarker final {
public:
    struct Resolution { std::unique_ptr<WaitEvent> event; CaptureResult result; };
    WaitMarker(CaptureGesture gesture, DWORD time, std::chrono::milliseconds timeout = std::chrono::seconds(30))
        : gesture(gesture), message_time(time), deadline_(std::chrono::steady_clock::now() + timeout) {}
    bool ready() { std::lock_guard<std::mutex> lock(mutex_); return ready_; }
    bool resolve(std::unique_ptr<WaitEvent> event, CaptureResult result) {
        { std::lock_guard<std::mutex> lock(mutex_);
          if (ready_) return false;
          event_ = std::move(event); result_ = result; ready_ = true; }
        changed_.notify_all();
        return true;
    }
    Resolution await() {
        std::unique_lock<std::mutex> lock(mutex_);
        if (!changed_.wait_until(lock, deadline_, [&] { return ready_; })) {
            ready_ = true;
            result_ = CaptureResult::TimedOut;
        }
        return {std::move(event_), result_};
    }
    const CaptureGesture gesture;
    const DWORD message_time;
private:
    std::mutex mutex_;
    std::condition_variable changed_;
    bool ready_ = false;
    CaptureResult result_ = CaptureResult::Cancelled;
    std::unique_ptr<WaitEvent> event_;
    const std::chrono::steady_clock::time_point deadline_;
};

// The FIFO owns the event wrapper; the source retains the reservation until
// resolution or termination, without borrowing collector-owned event memory.
class MarkerEvent final : public Event {
public:
    explicit MarkerEvent(std::shared_ptr<WaitMarker> value) : marker(std::move(value)) {}
    std::shared_ptr<WaitMarker> marker;
    std::unique_ptr<std::vector<unsigned char>> serialize() const override { throw std::logic_error("Capture marker"); }
    void playback() const override { throw std::logic_error("Capture marker"); }
};
}}
