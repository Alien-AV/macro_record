#pragma once
#include <Windows.h>
#include "../Common/KeyboardEvent.h"
#include "../Common/MouseEvent.h"

class WindowsInjectionAPI {
public:
	static bool inject_keyboard_event(WORD virtual_key_code, bool key_up);
	static bool inject_mouse_event(LONG x, LONG y, DWORD wheel_rotation, DWORD flags);
};