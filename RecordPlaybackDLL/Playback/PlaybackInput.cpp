#include "PlaybackInput.h"
#include "../Common/MouseTranslation.h"

bool WindowsInjectionAPI::playback_keyboard_event(const WORD virtual_key_code, const bool key_up)
{
	INPUT eventToInject = {}; // null everything
	eventToInject.type = INPUT_KEYBOARD;
	eventToInject.ki.wVk = virtual_key_code;
	if (key_up) {
		eventToInject.ki.dwFlags |= KEYEVENTF_KEYUP;
	}

	const UINT result = SendInput(1, &eventToInject, sizeof(eventToInject));
	if (result == 0) {
		DWORD lastError = GetLastError(); //TODO:handle this correctly
	}
	return (result != 0);
}

bool WindowsInjectionAPI::playback_mouse_event(LONG x, LONG y, DWORD mouse_data, bool relative_position, DWORD flags, bool mapped_to_virtual_desktop)
{
	using namespace record_playback::mouse;
	const auto bounds = !relative_position && (flags & MOUSEEVENTF_MOVE)
		? physical_desktop_bounds(mapped_to_virtual_desktop) : DesktopBounds{};
	std::vector<INPUT> inputs;
	if (!build_inputs(x, y, mouse_data, relative_position, flags, mapped_to_virtual_desktop, bounds, inputs)) return false;
	return submit_inputs(inputs, SendInput);
}
