#pragma once
#include <Windows.h>

class WindowsInjectionAPI {
public:
	static bool playback_keyboard_event(WORD virtual_key_code, bool key_up);
	static bool playback_mouse_event(LONG x, LONG y, DWORD mouse_data, bool relative_position, DWORD flags, bool mapped_to_virtual_desktop);
};
