#include "InjectInput.h"
#include "../InjectAndCaptureDll.h"
#include <thread>

bool WindowsInjectionAPI::InjectKeyboardEvent(WORD virtualKeyCode, bool keyUp)
{
	INPUT eventToInject = {}; // null everything
	eventToInject.type = INPUT_KEYBOARD;
	eventToInject.ki.wVk = virtualKeyCode;
	if (keyUp) {
		eventToInject.ki.dwFlags |= KEYEVENTF_KEYUP;
	}

	INPUT eventToInjectArr[1] = { eventToInject };
	UINT result = SendInput(1, &eventToInject, sizeof(eventToInject));
	if (result == 0) {
		DWORD lastError = GetLastError(); //TODO:handle this correctly
	}
	return (result != 0);
}

bool WindowsInjectionAPI::InjectMouseEvent(LONG x, LONG y, DWORD wheelRotation, DWORD flags)
{
	INPUT eventToInject = {}; // null everything
	eventToInject.type = INPUT_MOUSE;

	eventToInject.mi.dwFlags |= MOUSEEVENTF_ABSOLUTE;
	// mi.dx expects value between 0 and 65535, and converts it to pixel coords internally. since we store mouse positions by pixels, we need to do math.
	eventToInject.mi.dx = (x << 16) / GetSystemMetrics(SM_CXSCREEN); // multiply by 65536, then divide by screen size
	eventToInject.mi.dy = (y << 16) / GetSystemMetrics(SM_CYSCREEN); // GetDeviceCaps( hdcPrimaryMonitor, VERTRES)
	// SM_XVIRTUALSCREEN

	eventToInject.mi.dwFlags |= flags;

	INPUT eventToInjectArr[1] = { eventToInject };
	UINT result = SendInput(1, eventToInjectArr, sizeof(eventToInject));
	if (result == 0) {
		DWORD lastError = GetLastError();
	}
	return (result != 0);
}
