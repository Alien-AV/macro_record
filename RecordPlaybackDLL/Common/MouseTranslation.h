#pragma once
#include <Windows.h>
#include <chrono>
#include <cstdint>
#include <limits>
#include <vector>

namespace record_playback {
namespace mouse {

struct DesktopBounds {
    LONG left = 0;
    LONG top = 0;
    LONG width = 0;
    LONG height = 0;

    bool valid() const { return width > 0 && height > 0; }
};

// Query physical pixels even if the caller uses a different DPI context.
// Restore that context before returning; no window is created.
template <typename ReadMetric, typename ChangeDpiContext>
DesktopBounds physical_desktop_bounds(bool virtual_desktop, ReadMetric read_metric, ChangeDpiContext change_context)
{
    const auto previous = change_context(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    if (!previous) return {};
    DesktopBounds bounds;
    if (virtual_desktop) {
        bounds = { read_metric(SM_XVIRTUALSCREEN), read_metric(SM_YVIRTUALSCREEN),
            read_metric(SM_CXVIRTUALSCREEN), read_metric(SM_CYVIRTUALSCREEN) };
    }
    else {
        bounds = { 0, 0, read_metric(SM_CXSCREEN), read_metric(SM_CYSCREEN) };
    }
    change_context(previous);
    return bounds;
}

inline DesktopBounds physical_desktop_bounds(bool virtual_desktop)
{
    return physical_desktop_bounds(virtual_desktop, GetSystemMetrics, SetThreadDpiAwarenessContext);
}

inline std::int64_t clamp(std::int64_t value, std::int64_t low, std::int64_t high)
{
    return value < low ? low : value > high ? high : value;
}

inline LONG normalized_to_pixel(LONG value, LONG origin, LONG extent)
{
    if (extent <= 1) return origin;
    const auto normalized = clamp(value, 0, 65535);
    const auto offset = normalized == 65535 ? extent - 1 : normalized * extent / 65536;
    return static_cast<LONG>(clamp(static_cast<std::int64_t>(origin) + offset,
        (std::numeric_limits<LONG>::min)(), (std::numeric_limits<LONG>::max)()));
}

inline LONG pixel_to_normalized(LONG value, LONG origin, LONG extent)
{
    if (extent <= 1) return 0;
    const auto offset = clamp(static_cast<std::int64_t>(value) - origin, 0,
        static_cast<std::int64_t>(extent) - 1);
    if (offset == 0) return 0;
    if (offset == extent - 1) return 65535;
    // Invert floor(n * extent / 65536) with integer interval bounds. Truncating
    // the continuous centre can fall below the first valid n on wide desktops.
    // 65535 is reserved for the last pixel by normalized_to_pixel.
    const auto first = (offset * 65536 + extent - 1) / extent;
    const auto last = clamp(((offset + 1) * 65536 + extent - 1) / extent - 1, 0, 65534);
    if (first <= last) return static_cast<LONG>(first + (last - first) / 2);

    // Above 65536 pixels some intervals contain no integer. Choose the nearer
    // representable pixel, including the special last endpoint; ties go lower.
    const auto lower = static_cast<LONG>(clamp(first - 1, 0, 65534));
    const auto upper = static_cast<LONG>(clamp(first, 0, 65535));
    const auto lower_distance = offset - normalized_to_pixel(lower, 0, extent);
    const auto upper_distance = normalized_to_pixel(upper, 0, extent) - offset;
    return lower_distance <= upper_distance ? lower : upper;
}

struct RecordedAction {
    LONG x = 0;
    LONG y = 0;
    DWORD flags = 0;
    DWORD data = 0;
    bool relative = true;
    bool virtual_desktop = false;
    std::chrono::microseconds delay{};
};

// Pure translation. One raw packet has one timestamp, including split actions.
inline std::vector<RecordedAction> translate_raw_mouse(const RAWMOUSE& raw,
    const DesktopBounds& bounds, std::chrono::microseconds elapsed)
{
    std::vector<RecordedAction> actions;
    const bool absolute = (raw.usFlags & MOUSE_MOVE_ABSOLUTE) != 0;
    if (absolute && bounds.valid()) {
        actions.push_back({ normalized_to_pixel(raw.lLastX, bounds.left, bounds.width),
            normalized_to_pixel(raw.lLastY, bounds.top, bounds.height), MOUSEEVENTF_MOVE, 0,
            false, (raw.usFlags & MOUSE_VIRTUAL_DESKTOP) != 0, {} });
    }
    else if (!absolute && (raw.lLastX != 0 || raw.lLastY != 0)) {
        actions.push_back({ raw.lLastX, raw.lLastY, MOUSEEVENTF_MOVE, 0, true, false, {} });
    }

    struct ButtonMapping { USHORT raw_flag; DWORD input_flag; DWORD data; };
    const ButtonMapping buttons[] = {
        { RI_MOUSE_LEFT_BUTTON_DOWN, MOUSEEVENTF_LEFTDOWN, 0 },
        { RI_MOUSE_LEFT_BUTTON_UP, MOUSEEVENTF_LEFTUP, 0 },
        { RI_MOUSE_RIGHT_BUTTON_DOWN, MOUSEEVENTF_RIGHTDOWN, 0 },
        { RI_MOUSE_RIGHT_BUTTON_UP, MOUSEEVENTF_RIGHTUP, 0 },
        { RI_MOUSE_MIDDLE_BUTTON_DOWN, MOUSEEVENTF_MIDDLEDOWN, 0 },
        { RI_MOUSE_MIDDLE_BUTTON_UP, MOUSEEVENTF_MIDDLEUP, 0 },
        { RI_MOUSE_BUTTON_4_DOWN, MOUSEEVENTF_XDOWN, XBUTTON1 },
        { RI_MOUSE_BUTTON_4_UP, MOUSEEVENTF_XUP, XBUTTON1 },
        { RI_MOUSE_BUTTON_5_DOWN, MOUSEEVENTF_XDOWN, XBUTTON2 },
        { RI_MOUSE_BUTTON_5_UP, MOUSEEVENTF_XUP, XBUTTON2 },
    };
    for (const auto& button : buttons) {
        if (raw.usButtonFlags & button.raw_flag) {
            actions.push_back({ 0, 0, button.input_flag, button.data, true, false, {} });
        }
    }
    // usButtonData is a signed 16-bit wheel delta stored as uint32 wire bits.
    const auto delta = static_cast<DWORD>(static_cast<LONG>(static_cast<SHORT>(raw.usButtonData)));
    if (raw.usButtonFlags & RI_MOUSE_WHEEL) {
        actions.push_back({ 0, 0, MOUSEEVENTF_WHEEL, delta, true, false, {} });
    }
    if (raw.usButtonFlags & RI_MOUSE_HWHEEL) {
        actions.push_back({ 0, 0, MOUSEEVENTF_HWHEEL, delta, true, false, {} });
    }
    if (!actions.empty()) actions.front().delay = elapsed;
    return actions;
}

// Each meaning of mouseData needs its own packet, including combined flags in
// manually edited files. Coordinate modifiers come from the stored bool fields.
inline bool build_inputs(LONG x, LONG y, DWORD data, bool relative, DWORD flags,
    bool virtual_desktop, const DesktopBounds& bounds, std::vector<INPUT>& inputs)
{
    inputs.clear();
    if ((flags & MOUSEEVENTF_MOVE) && !relative && !bounds.valid()) return false;
    const auto add = [&inputs](DWORD action, DWORD payload) {
        INPUT input{};
        input.type = INPUT_MOUSE;
        input.mi.dwFlags = action;
        input.mi.mouseData = payload;
        inputs.push_back(input);
    };
    if (flags & MOUSEEVENTF_MOVE) {
        DWORD movement = MOUSEEVENTF_MOVE | (flags & MOUSEEVENTF_MOVE_NOCOALESCE);
        if (!relative) {
            movement |= MOUSEEVENTF_ABSOLUTE;
            if (virtual_desktop) movement |= MOUSEEVENTF_VIRTUALDESK;
        }
        add(movement, 0);
        inputs.back().mi.dx = relative ? x : pixel_to_normalized(x, bounds.left, bounds.width);
        inputs.back().mi.dy = relative ? y : pixel_to_normalized(y, bounds.top, bounds.height);
    }
    const DWORD buttons[] = { MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP,
        MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP, MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP };
    for (const auto button : buttons) {
        if (flags & button) add(button, 0);
    }
    // Old recordings captured only X1 and left its wheelRotation payload at zero.
    const DWORD x_buttons = data == 0 ? XBUTTON1 : data & (XBUTTON1 | XBUTTON2);
    if ((flags & MOUSEEVENTF_XDOWN) && x_buttons) add(MOUSEEVENTF_XDOWN, x_buttons);
    if ((flags & MOUSEEVENTF_XUP) && x_buttons) add(MOUSEEVENTF_XUP, x_buttons);
    if (flags & MOUSEEVENTF_WHEEL) add(MOUSEEVENTF_WHEEL, data);
    if (flags & MOUSEEVENTF_HWHEEL) add(MOUSEEVENTF_HWHEEL, data);
    return true;
}

// A fake sink can inspect INPUT packets. A partial batch must not be retried:
// resending its prefix could move or press a button twice.
template <typename Sink>
bool submit_inputs(std::vector<INPUT>& inputs, Sink sink)
{
    return inputs.empty() || sink(static_cast<UINT>(inputs.size()), inputs.data(), sizeof(INPUT)) == inputs.size();
}

} // namespace mouse
} // namespace record_playback
