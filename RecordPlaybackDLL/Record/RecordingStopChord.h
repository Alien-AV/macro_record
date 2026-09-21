#pragma once
#include <array>
#include <functional>
#include "RecordingPendingInput.h"

namespace record_playback { namespace capture {
enum StopGestures : uint32_t { NoStopGesture = 0, ControlW = 1, ControlR = 2, ControlAltF12 = 4, ControlShiftF12 = 8 };

inline uint32_t modifier(WORD key) {
    switch (key) {
    case VK_CONTROL: case VK_LCONTROL: case VK_RCONTROL: return MOD_CONTROL;
    case VK_MENU: case VK_LMENU: case VK_RMENU: return MOD_ALT;
    case VK_SHIFT: case VK_LSHIFT: case VK_RSHIFT: return MOD_SHIFT;
    case VK_LWIN: case VK_RWIN: return MOD_WIN;
    default: return 0;
    }
}
inline uint32_t gesture_modifiers(uint32_t gesture) {
    return gesture == ControlW || gesture == ControlR ? MOD_CONTROL
        : gesture == ControlAltF12 ? MOD_CONTROL | MOD_ALT
        : gesture == ControlShiftF12 ? MOD_CONTROL | MOD_SHIFT : 0;
}

inline uint32_t mouse_buttons(uint32_t held, DWORD flags, DWORD data) {
    const DWORD downs[] = {MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_MIDDLEDOWN};
    const DWORD ups[] = {MOUSEEVENTF_LEFTUP, MOUSEEVENTF_RIGHTUP, MOUSEEVENTF_MIDDLEUP};
    for (size_t i = 0; i < 3; ++i) {
        if (flags & downs[i]) held |= 1u << i;
        if (flags & ups[i]) held &= ~(1u << i);
    }
    const auto extra = ((data ? data : XBUTTON1) & (XBUTTON1 | XBUTTON2)) << 3;
    if (flags & MOUSEEVENTF_XDOWN) held |= extra;
    if (flags & MOUSEEVENTF_XUP) held &= ~extra;
    return held;
}

// Only a registered command can claim fresh, otherwise-unused keys. Plain
// movement is retained provisionally with them; modified interactions cancel
// the claim. Nothing already published to the recording is ever removed.
class StopChord {
public:
    using Keys = std::array<bool, 256>;
    using Sink = std::function<void(CapturedInput)>;
    explicit StopChord(Sink sink, PendingInput pending = PendingInput())
        : sink_(std::move(sink)), pending_(std::move(pending)) {}
    void start(uint32_t gestures, const Keys& held, uint32_t buttons = 0) {
        gestures_ = gestures;
        down_ = held;
        buttons_ = buttons;
        clear();
    }
    size_t pending_count() const { return pending_.memory_count(); }
    void input(std::unique_ptr<Event> event, DWORD time) {
        const auto key = dynamic_cast<KeyboardEvent*>(event.get());
        bool repeated = false;
        if (key && key->virtualKeyCode < down_.size()) {
            repeated = down_[key->virtualKeyCode];
            down_[key->virtualKeyCode] = !key->keyUp;
        }
        if (const auto mouse = dynamic_cast<MouseEvent*>(event.get())) {
            const bool dragging = buttons_ != 0;
            buttons_ = mouse_buttons(buttons_, mouse->ActionType, mouse->wheelRotation);
            const DWORD motion_flags = MOUSEEVENTF_MOVE | MOUSEEVENTF_MOVE_NOCOALESCE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
            if (!pending_.empty() && !dragging && !buttons_ && (mouse->ActionType & MOUSEEVENTF_MOVE)
                && !(mouse->ActionType & ~motion_flags)) {
                pending_.append(std::move(event), false);
                return;
            }
        }
        if (matched_ && key && key->virtualKeyCode < down_.size()) {
            // Repeats/releases of our own keys remain command candidates. A new
            // press after release is a new interaction, even on the same key.
            if (pending_keys_[key->virtualKeyCode] && repeated) {
                defer_key(std::move(event), time, false);
                return;
            }
            // A modifier held before this candidate is genuine, including its
            // repeats/releases behind the trigger; never erase its transitions.
            if ((modifier(key->virtualKeyCode) & gesture_modifiers(matched_)) && repeated) {
                pending_.append(std::move(event), false);
                return;
            }
        }
        if (!matched_ && key && !key->keyUp && key->virtualKeyCode < down_.size()) {
            const auto mods = modifiers();
            const auto gesture = key->virtualKeyCode == 'W' && mods == MOD_CONTROL ? ControlW
                : key->virtualKeyCode == 'R' && mods == MOD_CONTROL ? ControlR
                : key->virtualKeyCode == VK_F12 && mods == (MOD_CONTROL | MOD_ALT) ? ControlAltF12
                : key->virtualKeyCode == VK_F12 && mods == (MOD_CONTROL | MOD_SHIFT) ? ControlShiftF12 : NoStopGesture;
            if (!repeated && (gestures_ & gesture) && !nonmodifier_held(key->virtualKeyCode)) {
                matched_ = gesture;
                pending_keys_[key->virtualKeyCode] = true;
                defer_key(std::move(event), time, true);
                return;
            }
            if (modifier(key->virtualKeyCode) && possible_prefix(mods) && !buttons_
                && ((!repeated && !nonmodifier_held()) || pending_keys_[key->virtualKeyCode])) {
                pending_keys_[key->virtualKeyCode] = true;
                defer_key(std::move(event), time, !repeated);
                return;
            }
        }
        flush(false);
        sink_({std::move(event)});
    }
    void finish(uint32_t gesture, DWORD cutoff) {
        const auto mods = gesture_modifiers(gesture);
        const bool confirmed = mods && (gestures_ & gesture) && !pending_.empty()
            && static_cast<LONG>(first_time_ - cutoff) <= 0
            && static_cast<LONG>(last_press_time_ - cutoff) <= 0;
        // The trigger may still be queued (or absent from Raw Input). Only the
        // command's identity, registration and timestamp can confirm a prefix.
        flush(confirmed && (matched_ == gesture || (!matched_ && !(modifiers() & ~mods))));
    }
    void clear() { pending_.clear(); pending_keys_.fill(false); matched_ = NoStopGesture; }
private:
    uint32_t modifiers() const {
        uint32_t result = 0;
        for (WORD key = 0; key < down_.size(); ++key) if (down_[key]) result |= modifier(key);
        return result;
    }
    bool nonmodifier_held(WORD except = 0) const {
        for (WORD key = 0; key < down_.size(); ++key)
            if (key != except && down_[key] && !modifier(key)) return true;
        return false;
    }
    bool possible_prefix(uint32_t mods) const {
        for (auto gesture : {ControlW, ControlR, ControlAltF12, ControlShiftF12})
            if ((gestures_ & gesture) && !(mods & ~gesture_modifiers(gesture))) return true;
        return false;
    }
    void defer_key(std::unique_ptr<Event> event, DWORD time, bool fresh_press) {
        if (pending_.empty()) first_time_ = time;
        // Later repeats/releases still belong to the same physical press even
        // if an older hotkey message reaches capture after those raw events.
        if (fresh_press) last_press_time_ = time;
        pending_.append(std::move(event), true);
    }
    void flush(bool omit_command) {
        if (pending_.empty()) return;
        auto next = pending_.fresh();
        auto resolved = std::make_unique<PendingInput>(std::move(pending_));
        pending_ = std::move(next);
        clear();
        // The capture thread transfers ownership only. File reads and callbacks
        // belong to the collector, regardless of how large this prefix became.
        sink_({nullptr, std::move(resolved), omit_command});
    }
    Sink sink_;
    Keys down_{};
    Keys pending_keys_{};
    PendingInput pending_;
    uint32_t buttons_ = 0;
    uint32_t gestures_ = 0;
    uint32_t matched_ = NoStopGesture;
    DWORD first_time_ = 0, last_press_time_ = 0;
};
}}
