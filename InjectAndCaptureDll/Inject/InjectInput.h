#pragma once
#include <Windows.h>
#include "../Common/KeyboardEvent.h"
#include "../Common/MouseEvent.h"

class WindowsInjectionAPI {
public:
	static bool InjectKeyboardEvent(WORD virtualKeyCode, bool keyUp);
	static bool InjectMouseEvent(LONG x, LONG y, bool useRelativePosition, DWORD wheelRotation, DWORD flags);
};