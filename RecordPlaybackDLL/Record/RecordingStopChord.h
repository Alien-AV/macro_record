#pragma once
#include <array>
#include <functional>
#include "../Common/KeyboardEvent.h"

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

// Input remains provisional until an explicit hotkey stop confirms its provenance.
// Ordinary stop and every disambiguating input publish the original owning objects.
class StopChord {
public:
    using Keys = std::array<bool, 256>;
    using Sink = std::function<void(std::unique_ptr<Event>)>;
    static constexpr size_t capacity = 256;
    explicit StopChord(Sink sink) : sink_(std::move(sink)) {}
    void start(uint32_t gestures, const Keys& held) {
        gestures_ = gestures;
        down_ = held;
        clear();
    }
    size_t pending_count() const { return pending_.size(); }
    void input(std::unique_ptr<Event> event, DWORD time) {
        const auto key = dynamic_cast<KeyboardEvent*>(event.get());
        bool repeated = false;
        if (key && key->virtualKeyCode < down_.size()) {
            repeated = down_[key->virtualKeyCode];
            down_[key->virtualKeyCode] = !key->keyUp;
        }
        if (matched_) { defer(std::move(event), time); return; }
        if (key && !key->keyUp) {
            const auto mods = modifiers();
            const auto gesture = key->virtualKeyCode == 'W' && mods == MOD_CONTROL ? ControlW
                : key->virtualKeyCode == 'R' && mods == MOD_CONTROL ? ControlR
                : key->virtualKeyCode == VK_F12 && mods == (MOD_CONTROL | MOD_ALT) ? ControlAltF12
                : key->virtualKeyCode == VK_F12 && mods == (MOD_CONTROL | MOD_SHIFT) ? ControlShiftF12 : NoStopGesture;
            if (!repeated && (gestures_ & gesture)) {
                matched_ = gesture;
                defer(std::move(event), time);
                return;
            }
            if (modifier(key->virtualKeyCode) && possible_prefix(mods)
                && ((!repeated && !nonmodifier_held()) || pending_keys_[key->virtualKeyCode])) {
                pending_keys_[key->virtualKeyCode] = true;
                defer(std::move(event), time);
                return;
            }
        }
        flush();
        sink_(std::move(event));
    }
    void finish(uint32_t gesture, DWORD cutoff) {
        const auto mods = gesture_modifiers(gesture);
        const bool confirmed = mods && (gestures_ & gesture) && !pending_.empty()
            && static_cast<LONG>(first_time_ - cutoff) <= 0;
        // The trigger may still be queued (or never appear in Raw Input). Only
        // explicit command provenance can claim a fresh, otherwise-unused prefix.
        if (confirmed && (matched_ == gesture || (!matched_ && !(modifiers() & ~mods)))) clear();
        else flush();
    }
private:
    uint32_t modifiers() const {
        uint32_t result = 0;
        for (WORD key = 0; key < down_.size(); ++key) if (down_[key]) result |= modifier(key);
        return result;
    }
    bool nonmodifier_held() const {
        for (WORD key = 0; key < down_.size(); ++key) if (down_[key] && !modifier(key)) return true;
        return false;
    }
    bool possible_prefix(uint32_t mods) const {
        for (auto gesture : {ControlW, ControlR, ControlAltF12, ControlShiftF12})
            if ((gestures_ & gesture) && !(mods & ~gesture_modifiers(gesture))) return true;
        return false;
    }
    void defer(std::unique_ptr<Event> event, DWORD time) {
        if (pending_.empty()) first_time_ = time;
        pending_.push_back(std::move(event));
        // Preserve exact repeats/delays under pressure; never guess which to erase.
        if (pending_.size() == capacity) flush();
    }
    void clear() { pending_.clear(); pending_keys_.fill(false); matched_ = NoStopGesture; }
    void flush() {
        for (auto& event : pending_) sink_(std::move(event));
        clear();
    }
    Sink sink_;
    Keys down_{};
    Keys pending_keys_{};
    std::vector<std::unique_ptr<Event>> pending_;
    uint32_t gestures_ = 0;
    uint32_t matched_ = NoStopGesture;
    DWORD first_time_ = 0;
};
}}
