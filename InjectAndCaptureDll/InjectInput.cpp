#include "stdafx.h"
#include "InjectInput.h"


InjectInput::InjectInput()
{
}


InjectInput::~InjectInput()
{
}

bool InjectInput::InjectEvent(KeyboardEvent keyboardEvent)
{
	INPUT eventToInject = {}; // null everything
	eventToInject.type = INPUT_KEYBOARD;
	eventToInject.ki.wVk = keyboardEvent.virtualKeyCode;
	eventToInject.ki.wScan = keyboardEvent.hardwareScanCode;
	//eventToInject.ki.dwFlags = KEYEVENTF_SCANCODE;
	if (keyboardEvent.keyUp) {
		eventToInject.ki.dwFlags |= KEYEVENTF_KEYUP;
	}

	INPUT eventToInjectArr[1] = { eventToInject };
	UINT result = SendInput(1, &eventToInject, sizeof(eventToInject));
	if (result == 0) {
		lastError = GetLastError();
	}
	return (result != 0);
}

bool InjectInput::InjectEvent(MouseEvent mouseEvent)
{
	INPUT eventToInject = {}; // null everything
	
	eventToInject.type = INPUT_MOUSE;
	// mi.dx expects value between 0 and 65535, and converts it to pixel coords internally. since we store mouse positions by pixels, we need to do math.
	eventToInject.mi.dx = (mouseEvent.x << 16) / GetSystemMetrics(SM_CXSCREEN); // multiply by 65536, then divide by screen size
	eventToInject.mi.dy = (mouseEvent.y << 16) / GetSystemMetrics(SM_CYSCREEN); // GetDeviceCaps( hdcPrimaryMonitor, VERTRES)
	if (mouseEvent.useRelativePosition == false) {
		eventToInject.mi.dwFlags |= MOUSEEVENTF_ABSOLUTE;
	}

	eventToInject.mi.dwFlags |= mouseEvent.ActionType;

	INPUT eventToInjectArr[1] = { eventToInject };
	UINT result = SendInput(1, eventToInjectArr, sizeof(eventToInject));
	if (result == 0) {
		lastError = GetLastError();
	}
	return (result != 0);
}