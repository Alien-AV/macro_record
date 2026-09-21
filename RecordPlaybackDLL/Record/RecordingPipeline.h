#pragma once
#include "RecordingDelivery.h"
#include "../Common/MouseTranslation.h"

namespace record_playback { namespace capture {
// The production raw adapter and timing point are shared with deterministic
// fake-source tests. Source methods run only on the window thread; collect_one
// runs only on the collector. Neither draining nor a callback holds the FIFO lock.
class Pipeline {
public:
    using Clock = std::function<std::chrono::steady_clock::time_point()>;
    explicit Pipeline(Stream::Sink sink, DeliveryCollector::Failure failure,
        Clock clock = [] { return std::chrono::steady_clock::now(); }, PendingInput pending = PendingInput())
        : queue_(pending.fresh()), collector_(std::move(sink), std::move(failure)),
          stream_([this](Packet packet) { queue_.push(std::move(packet)); }, std::move(pending)),
          clock_(std::move(clock)) {}
    uint64_t session() const { return stream_.session(); }
    bool start(uint64_t session, uint32_t gestures = NoStopGesture, PointerOrigin origin = {}) {
        if (!stream_.start(session, gestures, origin)) return false;
        last_event_ = clock_();
        return true;
    }
    void stop(uint64_t session, uint32_t gesture = NoStopGesture, DWORD cutoff = 0) { stream_.stop(session, gesture, cutoff); }
    void fail(uint64_t session) { stream_.fail(session); }
    void keyboard(const RAWKEYBOARD& raw, DWORD time) {
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
    DeliveryQueue queue_;
    DeliveryCollector collector_;
    Stream stream_;
    Clock clock_;
    std::chrono::steady_clock::time_point last_event_{};
};
}}
