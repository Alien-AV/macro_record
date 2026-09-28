#pragma once
#include <atomic>
#include "RecordingDelivery.h"
#include "../Common/MouseTranslation.h"

namespace record_playback { namespace capture {
// The production raw adapter and timing point are shared with deterministic
// fake-source tests. Source methods run only on the window thread; collect_one
// runs only on the collector. Neither draining nor a callback holds the FIFO lock.
class Pipeline {
public:
    using Clock = std::function<std::chrono::steady_clock::time_point()>;
    using WakeSource = std::function<bool()>;
    explicit Pipeline(Stream::Sink sink, WakeSource wake,
        Clock clock = [] { return std::chrono::steady_clock::now(); }, PendingInput pending = PendingInput())
        : queue_(pending.fresh()), collector_(std::move(sink), [this, wake = std::move(wake)](uint64_t session) {
              // Publish before Failed reaches the client and can prompt a restart.
              // The post is only a wake-up hint: queue exhaustion cannot lose failure.
              pending_failure_.store(session, std::memory_order_release);
              (void)wake();
          }),
          stream_([this](Packet packet) { queue_.push(std::move(packet)); }, std::move(pending)),
          clock_(std::move(clock)) {}
    uint64_t session() const { return stream_.session(); }
    void process_failure() {
        // Source thread only. A late failure cannot terminate a newer session.
        stream_.fail(pending_failure_.exchange(0, std::memory_order_acq_rel));
    }
    bool start(uint64_t session, uint32_t gestures = NoStopGesture, PointerOrigin origin = {}, std::vector<CaptureGesture> captures = {}) {
        process_failure();
        if (!stream_.start(session, gestures, origin, std::move(captures))) return false;
        last_event_ = clock_();
        return true;
    }
    void stop(uint64_t session, uint32_t gesture = NoStopGesture, DWORD cutoff = 0) {
        process_failure();
        stream_.stop(session, gesture, cutoff);
    }
    void captured_wait(uint64_t session, CaptureGesture gesture, DWORD time, std::unique_ptr<WaitEvent> condition) {
        process_failure();
        stream_.captured_wait(session, gesture, time, std::move(condition));
    }
    void keyboard(const RAWKEYBOARD& raw, DWORD time) {
        process_failure();
        if (raw.VKey == 255) return;
        const auto key = sided_key(raw);
        const auto up = (raw.Flags & RI_KEY_BREAK) != 0;
        stream_.key(key, up);
        if (!session()) return;
        auto event = std::make_unique<KeyboardEvent>();
        event->virtualKeyCode = key;
        event->keyUp = up;
        event->time_since_last_event = elapsed();
        stream_.input(std::move(event), time);
    }
    void mouse(const RAWMOUSE& raw, const mouse::DesktopBounds& bounds, DWORD time) {
        process_failure();
        stream_.mouse(raw.usButtonFlags);
        if (!session()) return;
        auto actions = mouse::translate_raw_mouse(raw, bounds, std::chrono::microseconds(0));
        if (actions.empty()) return;
        actions.front().delay = elapsed();
        for (const auto& action : actions) {
            auto event = std::make_unique<MouseEvent>(action.x, action.y, action.flags,
                action.data, action.virtual_desktop, action.relative);
            event->time_since_last_event = action.delay;
            stream_.input(std::move(event), time);
        }
    }
    bool collect_one(bool wait = false) {
        Packet packet{};
        if (!queue_.take(packet, wait)) return false;
        collector_.input(std::move(packet));
        return true;
    }
    void close() { queue_.close(); }
    size_t queued_batches() const { return queue_.size(); }
private:
    std::chrono::microseconds elapsed() {
        const auto now = clock_();
        const auto delay = std::chrono::duration_cast<std::chrono::microseconds>(now - last_event_);
        last_event_ = now;
        return delay;
    }
    // One collector publishes failures in source FIFO order. A later failure can
    // replace an older one: that older source session has necessarily ended.
    // This fixed-size mailbox is independent of data/metadata delivery budgets.
    std::atomic<uint64_t> pending_failure_{0};
    DeliveryQueue queue_;
    DeliveryCollector collector_;
    Stream stream_;
    Clock clock_;
    std::chrono::steady_clock::time_point last_event_{};
};
}}
