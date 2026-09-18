#pragma once
#include <Windows.h>
#include <cstdint>
#include <functional>
#include <memory>
#include "../Common/Event.h"

namespace record_playback { namespace capture {
enum class Boundary : uint32_t { Started = 1, Stopped = 2, Failed = 3 };
enum HeldKeys : uint32_t { Q = 1, Control = 2, LeftControl = 4, RightControl = 8 };

inline WORD sided_key(const RAWKEYBOARD& data) {
    return data.VKey == VK_CONTROL ? ((data.Flags & RI_KEY_E0) ? VK_RCONTROL : VK_LCONTROL) : data.VKey;
}

struct Packet {
    uint64_t session;
    std::unique_ptr<Event> event;
    Boundary boundary{};
    uint32_t held_keys = 0;
};

// Capture-thread state only. Idle input updates three key bits; no idle event is retained.
class Stream {
public:
    using Sink = std::function<void(Packet)>;
    explicit Stream(Sink sink) : sink_(std::move(sink)) {}
    uint64_t session() const { return session_; }
    uint32_t held_keys() const { return held_; }
    void key(WORD key, bool up) {
        const uint32_t bit = key == 'Q' ? Q : key == VK_LCONTROL ? LeftControl
            : key == VK_RCONTROL ? RightControl : key == VK_CONTROL ? Control : 0;
        if (up) held_ &= ~bit;
        else held_ |= bit;
    }
    bool start(uint64_t session) {
        if (!session || session_) {
            sink_({session, nullptr, Boundary::Failed});
            return false;
        }
        session_ = session;
        sink_({session_, nullptr, Boundary::Started, held_});
        return true;
    }
    void input(std::unique_ptr<Event> event) {
        if (session_) sink_({session_, std::move(event)});
    }
    void stop(uint64_t session) {
        if (!session) return;
        if (session_ != session) return;
        session_ = 0;
        sink_({session, nullptr, Boundary::Stopped});
    }
private:
    Sink sink_;
    uint64_t session_ = 0;
    uint32_t held_ = 0;
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
