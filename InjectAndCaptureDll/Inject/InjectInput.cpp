#include "InjectInput.h"
#include <thread>

bool WindowsInjectionAPI::inject_keyboard_event(const WORD virtual_key_code, const bool key_up)
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

bool WindowsInjectionAPI::inject_mouse_event(LONG x, LONG y, DWORD wheel_rotation, bool relative_position, const DWORD flags) //TODO: implement wheel rotation injection
{
	INPUT eventToInject = {}; // null everything
	eventToInject.type = INPUT_MOUSE;
	//if (previousX == 0 && previousY == 0) {
	if (!relative_position) {
		eventToInject.mi.dwFlags |= MOUSEEVENTF_ABSOLUTE;
		// mi.dx expects value between 0 and 65535, and converts it to pixel coords internally. since we store mouse positions by pixels, we need to do math.
		eventToInject.mi.dx = (x << 16) / (GetSystemMetrics(SM_CXSCREEN) - 1); // multiply by 65536, then divide by screen size
		eventToInject.mi.dy = (y << 16) / (GetSystemMetrics(SM_CYSCREEN) - 1); // GetDeviceCaps( hdcPrimaryMonitor, VERTRES)
		// SM_XVIRTUALSCREEN
	}
	else {
		eventToInject.mi.dx = x;
		eventToInject.mi.dy = y;
	}

	eventToInject.mi.dwFlags |= flags;

	INPUT eventToInjectArr[1] = { eventToInject };
	const UINT result = SendInput(1, eventToInjectArr, sizeof(eventToInject));
	if (result == 0) {
		DWORD lastError = GetLastError(); //TODO:handle this correctly
	}
	return (result != 0);
}
