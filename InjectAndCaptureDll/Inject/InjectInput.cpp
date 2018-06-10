#include "InjectInput.h"
#include "../InjectAndCaptureDll.h"
#include <thread>

LONG previous_x=0, previous_y=0;

bool WindowsInjectionAPI::inject_keyboard_event(const WORD virtual_key_code, const bool key_up)
{
	INPUT eventToInject = {}; // null everything
	eventToInject.type = INPUT_KEYBOARD;
	eventToInject.ki.wVk = virtual_key_code;
	if (key_up) {
		eventToInject.ki.dwFlags |= KEYEVENTF_KEYUP;
	}

	INPUT eventToInjectArr[1] = { eventToInject };
	UINT result = SendInput(1, &eventToInject, sizeof(eventToInject));
	if (result == 0) {
		DWORD lastError = GetLastError(); //TODO:handle this correctly
	}
	return (result != 0);
}

bool WindowsInjectionAPI::inject_mouse_event(LONG x, LONG y, DWORD wheel_rotation, const DWORD flags)
{
	INPUT eventToInject = {}; // null everything
	eventToInject.type = INPUT_MOUSE;
	//if (previousX == 0 && previousY == 0) {
	if (true) {
		eventToInject.mi.dwFlags |= MOUSEEVENTF_ABSOLUTE;
		// mi.dx expects value between 0 and 65535, and converts it to pixel coords internally. since we store mouse positions by pixels, we need to do math.
		eventToInject.mi.dx = (x << 16) / (GetSystemMetrics(SM_CXSCREEN) - 1); // multiply by 65536, then divide by screen size
		eventToInject.mi.dy = (y << 16) / (GetSystemMetrics(SM_CYSCREEN) - 1); // GetDeviceCaps( hdcPrimaryMonitor, VERTRES)
		// SM_XVIRTUALSCREEN
	}
	else {
		eventToInject.mi.dx = x - previous_x;
		eventToInject.mi.dy = y - previous_y;
	}

	previous_x = x;
	previous_y = y;

	eventToInject.mi.dwFlags |= flags;

	INPUT eventToInjectArr[1] = { eventToInject };
	UINT result = SendInput(1, eventToInjectArr, sizeof(eventToInject));
	if (result == 0) {
		DWORD lastError = GetLastError();
	}
	return (result != 0);
}
