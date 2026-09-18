#include "pch.h"
#include "../RecordPlaybackDLL/Common/MouseTranslation.h"

namespace {
using namespace record_playback::mouse;
using std::chrono::microseconds;

const DesktopBounds primary{ 0, 0, 3840, 2160 };
// A 4K primary and a 1920x1080 display left/above it. Bounds are physical pixels;
// monitor scale factors must not be applied to these supplied metrics again.
const DesktopBounds desktop{ -1920, -1080, 5760, 3240 };

TEST(MouseTranslation, MetricsUseTemporaryPhysicalDpiContextAndSelectedDesktop)
{
    for (const bool virtual_desktop : { false, true }) {
        auto context = DPI_AWARENESS_CONTEXT_UNAWARE;
        int metric_calls = 0;
        int context_calls = 0;
        const auto bounds = physical_desktop_bounds(virtual_desktop,
            [&context, &metric_calls](int metric) {
                ++metric_calls;
                EXPECT_EQ(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2, context);
                switch (metric) {
                case SM_CXSCREEN: return 3840;
                case SM_CYSCREEN: return 2160;
                case SM_XVIRTUALSCREEN: return -1920;
                case SM_YVIRTUALSCREEN: return -1080;
                case SM_CXVIRTUALSCREEN: return 5760;
                case SM_CYVIRTUALSCREEN: return 3240;
                default: ADD_FAILURE() << "Unexpected metric"; return 0;
                }
            }, [&context, &context_calls](DPI_AWARENESS_CONTEXT next) {
                ++context_calls;
                const auto previous = context;
                context = next;
                return previous;
            });
        EXPECT_EQ(virtual_desktop ? 4 : 2, metric_calls);
        EXPECT_EQ(2, context_calls);
        EXPECT_EQ(DPI_AWARENESS_CONTEXT_UNAWARE, context);
        EXPECT_EQ(virtual_desktop ? desktop.left : primary.left, bounds.left);
        EXPECT_EQ(virtual_desktop ? desktop.top : primary.top, bounds.top);
        EXPECT_EQ(virtual_desktop ? desktop.width : primary.width, bounds.width);
        EXPECT_EQ(virtual_desktop ? desktop.height : primary.height, bounds.height);
    }
}

TEST(MouseTranslation, DpiContextFailureDoesNotUseVirtualizedMetrics)
{
    const auto bounds = physical_desktop_bounds(true,
        [](int) { ADD_FAILURE() << "Unsafe metric query"; return 100; },
        [](DPI_AWARENESS_CONTEXT) -> DPI_AWARENESS_CONTEXT { return nullptr; });
    EXPECT_FALSE(bounds.valid());
}

TEST(MouseTranslation, AbsoluteRawCoordinatesBecomePrimaryPixels)
{
    RAWMOUSE raw{};
    raw.usFlags = MOUSE_MOVE_ABSOLUTE;
    raw.lLastX = 32768;
    raw.lLastY = 65535;
    const auto actions = translate_raw_mouse(raw, primary, microseconds(987));
    ASSERT_EQ(1u, actions.size());
    EXPECT_EQ(1920, actions[0].x);
    EXPECT_EQ(2159, actions[0].y);
    EXPECT_FALSE(actions[0].relative);
    EXPECT_FALSE(actions[0].virtual_desktop);
    EXPECT_EQ(microseconds(987), actions[0].delay);
}

TEST(MouseTranslation, VirtualAbsoluteCoordinatesIncludeNegativeOrigin)
{
    RAWMOUSE raw{};
    raw.usFlags = MOUSE_MOVE_ABSOLUTE | MOUSE_VIRTUAL_DESKTOP;
    const auto top_left = translate_raw_mouse(raw, desktop, microseconds(1));
    ASSERT_EQ(1u, top_left.size());
    EXPECT_EQ(-1920, top_left[0].x);
    EXPECT_EQ(-1080, top_left[0].y);
    EXPECT_TRUE(top_left[0].virtual_desktop);
    raw.lLastX = raw.lLastY = 65535;
    const auto bottom_right = translate_raw_mouse(raw, desktop, microseconds(1));
    EXPECT_EQ(3839, bottom_right[0].x);
    EXPECT_EQ(2159, bottom_right[0].y);
}

TEST(MouseTranslation, RelativeCountsAreNotNormalizedOrScaled)
{
    RAWMOUSE raw{};
    raw.lLastX = -70000;
    raw.lLastY = 17;
    raw.usFlags = MOUSE_VIRTUAL_DESKTOP | MOUSE_MOVE_NOCOALESCE;
    const auto actions = translate_raw_mouse(raw, desktop, microseconds(9));
    ASSERT_EQ(1u, actions.size());
    EXPECT_EQ(-70000, actions[0].x);
    EXPECT_EQ(17, actions[0].y);
    EXPECT_TRUE(actions[0].relative);
    EXPECT_FALSE(actions[0].virtual_desktop);
}

TEST(MouseTranslation, EmptyPacketsProduceNoEventsButAbsoluteOriginIsAMove)
{
    RAWMOUSE raw{};
    raw.usFlags = MOUSE_ATTRIBUTES_CHANGED;
    EXPECT_TRUE(translate_raw_mouse(raw, primary, microseconds(12)).empty());
    raw.usFlags = MOUSE_MOVE_ABSOLUTE;
    const auto actions = translate_raw_mouse(raw, primary, microseconds(12));
    ASSERT_EQ(1u, actions.size());
    EXPECT_EQ(DWORD{ MOUSEEVENTF_MOVE }, actions[0].flags);
    EXPECT_EQ(0, actions[0].x);
    EXPECT_EQ(0, actions[0].y);
}

TEST(MouseTranslation, WheelsSignExtendBothAxesAndKeepFineResolutionDeltas)
{
    for (const auto raw_flag : { RI_MOUSE_WHEEL, RI_MOUSE_HWHEEL }) {
        const SHORT deltas[] = { -32768, -120, -1, 0, 1, 30, 120, 32767 };
        for (const auto delta : deltas) {
            RAWMOUSE raw{};
            raw.usButtonFlags = static_cast<USHORT>(raw_flag);
            raw.usButtonData = static_cast<USHORT>(delta);
            const auto actions = translate_raw_mouse(raw, {}, microseconds(40));
            ASSERT_EQ(1u, actions.size());
            EXPECT_EQ(static_cast<DWORD>(raw_flag == RI_MOUSE_WHEEL ? MOUSEEVENTF_WHEEL : MOUSEEVENTF_HWHEEL), actions[0].flags);
            EXPECT_EQ(static_cast<DWORD>(static_cast<LONG>(delta)), actions[0].data);
            EXPECT_EQ(microseconds(40), actions[0].delay);
            std::vector<INPUT> inputs;
            ASSERT_TRUE(build_inputs(0, 0, actions[0].data, true, actions[0].flags, false, {}, inputs));
            ASSERT_EQ(1u, inputs.size());
            EXPECT_EQ(actions[0].data, inputs[0].mi.mouseData);
            EXPECT_EQ(actions[0].flags, inputs[0].mi.dwFlags);
        }
    }
}

TEST(MouseTranslation, XButtonsKeepTheirIdentityAndTransition)
{
    const USHORT raw_flags[] = { RI_MOUSE_BUTTON_4_DOWN, RI_MOUSE_BUTTON_4_UP,
        RI_MOUSE_BUTTON_5_DOWN, RI_MOUSE_BUTTON_5_UP };
    const DWORD flags[] = { MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP };
    const DWORD payloads[] = { XBUTTON1, XBUTTON1, XBUTTON2, XBUTTON2 };
    for (size_t index = 0; index < 4; ++index) {
        RAWMOUSE raw{};
        raw.usButtonFlags = raw_flags[index];
        raw.usButtonData = 999; // Only wheels use this field in RAWMOUSE.
        const auto actions = translate_raw_mouse(raw, {}, microseconds(20));
        ASSERT_EQ(1u, actions.size());
        EXPECT_EQ(flags[index], actions[0].flags);
        EXPECT_EQ(payloads[index], actions[0].data);
        std::vector<INPUT> inputs;
        ASSERT_TRUE(build_inputs(0, 0, actions[0].data, true, actions[0].flags, false, {}, inputs));
        ASSERT_EQ(1u, inputs.size());
        EXPECT_EQ(payloads[index], inputs[0].mi.mouseData);
    }
}

TEST(MouseTranslation, CompoundRawPacketPreservesAllActionsAndOneDelay)
{
    RAWMOUSE raw{};
    raw.lLastX = 15;
    raw.lLastY = -8;
    raw.usButtonFlags = RI_MOUSE_LEFT_BUTTON_DOWN | RI_MOUSE_BUTTON_4_DOWN |
        RI_MOUSE_BUTTON_5_UP | RI_MOUSE_WHEEL | RI_MOUSE_HWHEEL;
    raw.usButtonData = static_cast<USHORT>(-120);
    const auto actions = translate_raw_mouse(raw, {}, microseconds(123456));
    ASSERT_EQ(6u, actions.size());
    const DWORD expected[] = { MOUSEEVENTF_MOVE, MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_XDOWN,
        MOUSEEVENTF_XUP, MOUSEEVENTF_WHEEL, MOUSEEVENTF_HWHEEL };
    for (size_t index = 0; index < actions.size(); ++index) {
        EXPECT_EQ(expected[index], actions[index].flags);
        EXPECT_EQ(microseconds(index == 0 ? 123456 : 0), actions[index].delay);
    }
    EXPECT_EQ(DWORD{ XBUTTON1 }, actions[2].data);
    EXPECT_EQ(DWORD{ XBUTTON2 }, actions[3].data);
    EXPECT_EQ(static_cast<DWORD>(-120), actions[4].data);
    EXPECT_EQ(static_cast<DWORD>(-120), actions[5].data);
}

TEST(MouseTranslation, AllOrdinaryButtonTransitionsSurviveCaptureAndReplay)
{
    RAWMOUSE raw{};
    raw.usButtonFlags = RI_MOUSE_LEFT_BUTTON_DOWN | RI_MOUSE_LEFT_BUTTON_UP |
        RI_MOUSE_RIGHT_BUTTON_DOWN | RI_MOUSE_RIGHT_BUTTON_UP |
        RI_MOUSE_MIDDLE_BUTTON_DOWN | RI_MOUSE_MIDDLE_BUTTON_UP;
    const auto actions = translate_raw_mouse(raw, {}, microseconds(50));
    ASSERT_EQ(6u, actions.size());
    DWORD all_flags = 0;
    for (const auto& action : actions) {
        EXPECT_EQ(0u, action.data);
        EXPECT_EQ(0u, action.flags & MOUSEEVENTF_MOVE);
        all_flags |= action.flags;
    }
    std::vector<INPUT> inputs;
    ASSERT_TRUE(build_inputs(0, 0, 123, false, all_flags, false, {}, inputs));
    ASSERT_EQ(6u, inputs.size());
    for (size_t index = 0; index < inputs.size(); ++index) {
        EXPECT_EQ(actions[index].flags, inputs[index].mi.dwFlags);
        EXPECT_EQ(0u, inputs[index].mi.mouseData);
    }
}

TEST(MouseTranslation, AbsoluteReplayUsesVirtualOriginAndPhysicalExtent)
{
    std::vector<INPUT> inputs;
    ASSERT_TRUE(build_inputs(-960, -540, 0, false, MOUSEEVENTF_MOVE, true, desktop, inputs));
    ASSERT_EQ(1u, inputs.size());
    EXPECT_EQ(DWORD{ MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK }, inputs[0].mi.dwFlags);
    EXPECT_EQ(-960, normalized_to_pixel(inputs[0].mi.dx, desktop.left, desktop.width));
    EXPECT_EQ(-540, normalized_to_pixel(inputs[0].mi.dy, desktop.top, desktop.height));
    EXPECT_EQ(0u, inputs[0].mi.mouseData);
    ASSERT_TRUE(build_inputs(1920, 1080, 0, false, MOUSEEVENTF_MOVE, false, primary, inputs));
    EXPECT_EQ(DWORD{ MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE }, inputs[0].mi.dwFlags);
    EXPECT_EQ(1920, normalized_to_pixel(inputs[0].mi.dx, 0, primary.width));
}

TEST(MouseTranslation, EveryPixelRoundTripsAcrossCommonDesktopWidths)
{
    for (const LONG extent : { 1, 2, 1920, 3840, 5760, 16384, 65536 }) {
        for (LONG offset = 0; offset < extent; ++offset) {
            const LONG pixel = -4000 + offset;
            const auto normalized = pixel_to_normalized(pixel, -4000, extent);
            ASSERT_GE(normalized, 0);
            ASSERT_LE(normalized, 65535);
            ASSERT_EQ(pixel, normalized_to_pixel(normalized, -4000, extent));
        }
    }
}

TEST(MouseTranslation, NormalizationClampsAndDoesNotOverflow)
{
    const auto lowest = (std::numeric_limits<LONG>::min)();
    const auto highest = (std::numeric_limits<LONG>::max)();
    EXPECT_EQ(0, pixel_to_normalized(lowest, -1920, 5760));
    EXPECT_EQ(65535, pixel_to_normalized(highest, -1920, 5760));
    EXPECT_EQ(65535, pixel_to_normalized(highest, lowest, highest));
    EXPECT_EQ(0, pixel_to_normalized(lowest, highest, highest));
    EXPECT_EQ(-1920, normalized_to_pixel(lowest, -1920, 5760));
    EXPECT_EQ(3839, normalized_to_pixel(highest, -1920, 5760));
    EXPECT_EQ(highest, normalized_to_pixel(65535, highest, highest));
    EXPECT_EQ(highest - 1, normalized_to_pixel(65535, 0, highest));
    EXPECT_EQ(0, pixel_to_normalized(999, 5, 1));
    EXPECT_EQ(5, normalized_to_pixel(65535, 5, 1));
}

TEST(MouseTranslation, InvalidBoundsFailOnlyAbsoluteMovement)
{
    std::vector<INPUT> inputs(1);
    EXPECT_FALSE(build_inputs(0, 0, 0, false, MOUSEEVENTF_MOVE | MOUSEEVENTF_LEFTDOWN, true, {}, inputs));
    EXPECT_TRUE(inputs.empty());
    EXPECT_TRUE(build_inputs(0, 0, 0, false, MOUSEEVENTF_LEFTUP, true, {}, inputs));
    ASSERT_EQ(1u, inputs.size());
    EXPECT_EQ(DWORD{ MOUSEEVENTF_LEFTUP }, inputs[0].mi.dwFlags);
    EXPECT_TRUE(build_inputs(-4, 9, 0, true, MOUSEEVENTF_MOVE, false, {}, inputs));
    EXPECT_EQ(-4, inputs[0].mi.dx);
    EXPECT_EQ(9, inputs[0].mi.dy);
}

TEST(MouseTranslation, InvalidCaptureBoundsDoNotLoseButtonTransitions)
{
    RAWMOUSE raw{};
    raw.usFlags = MOUSE_MOVE_ABSOLUTE;
    raw.usButtonFlags = RI_MOUSE_LEFT_BUTTON_UP;
    const auto actions = translate_raw_mouse(raw, {}, microseconds(77));
    ASSERT_EQ(1u, actions.size());
    EXPECT_EQ(DWORD{ MOUSEEVENTF_LEFTUP }, actions[0].flags);
    EXPECT_EQ(microseconds(77), actions[0].delay);
}

TEST(MouseTranslation, RelativeReplayIgnoresAbsoluteModifiersAndKeepsCounts)
{
    std::vector<INPUT> inputs;
    ASSERT_TRUE(build_inputs(-555, 777, 12, true, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE |
        MOUSEEVENTF_VIRTUALDESK | MOUSEEVENTF_MOVE_NOCOALESCE, true, desktop, inputs));
    ASSERT_EQ(1u, inputs.size());
    EXPECT_EQ(-555, inputs[0].mi.dx);
    EXPECT_EQ(777, inputs[0].mi.dy);
    EXPECT_EQ(DWORD{ MOUSEEVENTF_MOVE | MOUSEEVENTF_MOVE_NOCOALESCE }, inputs[0].mi.dwFlags);
    EXPECT_EQ(0u, inputs[0].mi.mouseData);
}

TEST(MouseTranslation, LegacyZeroPayloadXActionsMeanX1)
{
    for (const DWORD flags : { MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP }) {
        std::vector<INPUT> inputs;
        ASSERT_TRUE(build_inputs(0, 0, 0, true, flags, false, {}, inputs));
        ASSERT_EQ(1u, inputs.size());
        EXPECT_EQ(DWORD{ XBUTTON1 }, inputs[0].mi.mouseData);
        EXPECT_EQ(flags, inputs[0].mi.dwFlags);
    }
}

TEST(MouseTranslation, XButtonMasksAndWheelPayloadsUseSeparatePackets)
{
    std::vector<INPUT> inputs;
    ASSERT_TRUE(build_inputs(1, 2, XBUTTON1 | XBUTTON2, true,
        MOUSEEVENTF_MOVE | MOUSEEVENTF_XDOWN | MOUSEEVENTF_XUP | MOUSEEVENTF_WHEEL | MOUSEEVENTF_HWHEEL,
        false, {}, inputs));
    ASSERT_EQ(5u, inputs.size());
    EXPECT_EQ(0u, inputs[0].mi.mouseData);
    EXPECT_EQ(DWORD{ MOUSEEVENTF_XDOWN }, inputs[1].mi.dwFlags);
    EXPECT_EQ(DWORD{ MOUSEEVENTF_XUP }, inputs[2].mi.dwFlags);
    EXPECT_EQ(DWORD{ MOUSEEVENTF_WHEEL }, inputs[3].mi.dwFlags);
    EXPECT_EQ(DWORD{ MOUSEEVENTF_HWHEEL }, inputs[4].mi.dwFlags);
    EXPECT_EQ(DWORD{ XBUTTON1 | XBUTTON2 }, inputs[1].mi.mouseData);
    EXPECT_EQ(DWORD{ XBUTTON1 | XBUTTON2 }, inputs[2].mi.mouseData);
}

TEST(MouseTranslation, FakeSinkReceivesZeroInitializedPacketsAndFullBatch)
{
    std::vector<INPUT> inputs;
    ASSERT_TRUE(build_inputs(-2, 5, XBUTTON2, true, MOUSEEVENTF_MOVE | MOUSEEVENTF_XDOWN, false, {}, inputs));
    int calls = 0;
    const bool result = submit_inputs(inputs, [&calls](UINT count, INPUT* packets, int size) -> UINT {
        ++calls;
        EXPECT_EQ(2u, count);
        EXPECT_EQ(sizeof(INPUT), static_cast<size_t>(size));
        EXPECT_EQ(DWORD{ INPUT_MOUSE }, packets[0].type);
        EXPECT_EQ(-2, packets[0].mi.dx);
        EXPECT_EQ(DWORD{ XBUTTON2 }, packets[1].mi.mouseData);
        EXPECT_EQ(0u, packets[0].mi.time);
        EXPECT_EQ(0u, packets[1].mi.dwExtraInfo);
        return count;
    });
    EXPECT_TRUE(result);
    EXPECT_EQ(1, calls);
}

TEST(MouseTranslation, PartialAndFailedFakeSinksAreNotRetried)
{
    std::vector<INPUT> inputs;
    ASSERT_TRUE(build_inputs(0, 0, 0, true, MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, false, {}, inputs));
    for (UINT accepted : { 0u, 1u }) {
        int calls = 0;
        EXPECT_FALSE(submit_inputs(inputs, [&calls, accepted](UINT, INPUT*, int) { ++calls; return accepted; }));
        EXPECT_EQ(1, calls);
    }
    inputs.clear();
    EXPECT_TRUE(submit_inputs(inputs, [](UINT, INPUT*, int) { ADD_FAILURE() << "Empty batch reached sink"; return 0u; }));
}

} // namespace
