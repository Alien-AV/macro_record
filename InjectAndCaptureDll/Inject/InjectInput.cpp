#include "InjectInput.h"
#include "../InjectAndCaptureDll.h"
#include <thread>

InjectInput::InjectInput()
{
}


InjectInput::~InjectInput()
{
}

bool InjectEvent(IdleEvent idleEvent)
{
	std::this_thread::sleep_for(idleEvent.duration);
	return true;
}

bool InjectEvent(KeyboardEvent keyboardEvent)
{
	INPUT eventToInject = {}; // null everything
	eventToInject.type = INPUT_KEYBOARD;
	eventToInject.ki.wVk = keyboardEvent.virtualKeyCode;
	//eventToInject.ki.wScan = keyboardEvent.hardwareScanCode;
	//eventToInject.ki.dwFlags = KEYEVENTF_SCANCODE;
	if (keyboardEvent.keyUp) {
		eventToInject.ki.dwFlags |= KEYEVENTF_KEYUP;
	}

	INPUT eventToInjectArr[1] = { eventToInject };
	UINT result = SendInput(1, &eventToInject, sizeof(eventToInject));
	if (result == 0) {
		DWORD lastError = GetLastError(); //TODO:handle this correctly
	}
	return (result != 0);
}

bool InjectEvent(MouseEvent mouseEvent)
{
	INPUT eventToInject = {}; // null everything
	
	eventToInject.type = INPUT_MOUSE;
	if (mouseEvent.useRelativePosition == false) {
		eventToInject.mi.dwFlags |= MOUSEEVENTF_ABSOLUTE;
		// mi.dx expects value between 0 and 65535, and converts it to pixel coords internally. since we store mouse positions by pixels, we need to do math.
		eventToInject.mi.dx = (mouseEvent.x << 16) / GetSystemMetrics(SM_CXSCREEN); // multiply by 65536, then divide by screen size
		eventToInject.mi.dy = (mouseEvent.y << 16) / GetSystemMetrics(SM_CYSCREEN); // GetDeviceCaps( hdcPrimaryMonitor, VERTRES)
																					// SM_XVIRTUALSCREEN
	}
	else {
		eventToInject.mi.dx = mouseEvent.x;
		eventToInject.mi.dy = mouseEvent.y;
	}
	
	eventToInject.mi.dwFlags |= mouseEvent.ActionType;

	INPUT eventToInjectArr[1] = { eventToInject };
	UINT result = SendInput(1, eventToInjectArr, sizeof(eventToInject));
	if (result == 0) {
		DWORD lastError = GetLastError();
	}
	return (result != 0);
}