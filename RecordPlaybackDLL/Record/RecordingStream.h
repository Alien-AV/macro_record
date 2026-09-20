#pragma once
#include <Windows.h>
#include <cstdint>
#include <functional>
#include <memory>
#include "../Common/Event.h"
#include "RecordingStopChord.h"

namespace record_playback { namespace capture {
enum class Boundary : uint32_t { Started = 1, Stopped = 2, Failed = 3 };
enum HeldKeys : uint32_t { Q = 1, Control = 2, LeftControl = 4, RightControl = 8 };
struct PointerOrigin { int32_t x = 0; int32_t y = 0; bool valid = false; };

inline WORD sided_key(const RAWKEYBOARD& data) {
    if (data.VKey == VK_CONTROL) return (data.Flags & RI_KEY_E0) ? VK_RCONTROL : VK_LCONTROL;
    if (data.VKey == VK_MENU) return (data.Flags & RI_KEY_E0) ? VK_RMENU : VK_LMENU;
    if (data.VKey == VK_SHIFT) return data.MakeCode == 0x36 ? VK_RSHIFT : VK_LSHIFT;
    return data.VKey;
}

struct Packet {
    uint64_t session;
    std::unique_ptr<Event> event;
    Boundary boundary{};
    uint32_t held_keys = 0;
    uint32_t idle_released_keys = 0;
    PointerOrigin origin{};
};

// Capture-thread state only. Idle input updates chord state/release bits, not an event log.
class Stream {
public:
    using Sink = std::function<void(Packet)>;
    explicit Stream(Sink sink) : sink_(std::move(sink)), stop_chord_([this](std::unique_ptr<Event> event) {
        sink_({session_, std::move(event)});
    }) {}
    uint64_t session() const { return session_; }
    uint32_t held_keys() const { return held_; }
    void key(WORD key, bool up) {
        if (key < physical_keys_.size()) physical_keys_[key] = !up;
        const uint32_t bit = key == 'Q' ? Q : key == VK_LCONTROL ? LeftControl
            : key == VK_RCONTROL ? RightControl : key == VK_CONTROL ? Control : 0;
        if (up) {
            held_ &= ~bit;
            if (!session_) idle_released_ |= bit;
        }
        else held_ |= bit;
    }
    bool start(uint64_t session, uint32_t stop_gestures = NoStopGesture, PointerOrigin origin = {}) {
        if (!session || session_) {
            sink_({session, nullptr, Boundary::Failed});
            return false;
        }
        session_ = session;
        stop_chord_.start(stop_gestures, physical_keys_);
        sink_({session_, nullptr, Boundary::Started, held_, idle_released_, origin});
        idle_released_ = 0;
        return true;
    }
    void input(std::unique_ptr<Event> event, DWORD time = 0) {
        if (session_) stop_chord_.input(std::move(event), time);
    }
    void stop(uint64_t session, uint32_t gesture = NoStopGesture, DWORD cutoff = 0) {
        if (!session) return;
        if (session_ != session) return;
        stop_chord_.finish(gesture, cutoff);
        session_ = 0;
        idle_released_ = 0;
        sink_({session, nullptr, Boundary::Stopped});
    }
private:
    Sink sink_;
    uint64_t session_ = 0;
    uint32_t held_ = 0;
    uint32_t idle_released_ = 0;
    StopChord::Keys physical_keys_{};
    StopChord stop_chord_;
};

// DWORD message times wrap. Commands live far less than half the 49-day tick range.
inline bool at_or_before(DWORD time, DWORD cutoff) {
    return static_cast<LONG>(time - cutoff) <= 0;
}

// Consume only the finite prefix through this command's timestamp, in bounded chunks.
// peek leaves the first newer message queued; no asynchronous key-state sampling occurs.
template<class Peek, class Consume>
bool drain_prefix(DWORD cutoff, Peek peek, Consume consume, size_t budget = 256) {
    DWORD time;
    while (budget-- && peek(time) && at_or_before(time, cutoff)) consume();
    return !peek(time) || !at_or_before(time, cutoff);
}
}}
