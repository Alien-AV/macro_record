#include "..\stdafx.h"
#include "CaptureInput.h"


CaptureInput::CaptureInput()
{
	RAWINPUTDEVICE Rid[2];

	Rid[0].usUsagePage = 0x01;
	Rid[0].usUsage = 0x02;
	Rid[0].dwFlags = RIDEV_NOLEGACY;   // adds HID mouse and also ignores legacy mouse messages
	Rid[0].hwndTarget = 0;

	Rid[1].usUsagePage = 0x01;
	Rid[1].usUsage = 0x06;
	Rid[1].dwFlags = RIDEV_NOLEGACY;   // adds HID keyboard and also ignores legacy keyboard messages
	Rid[1].hwndTarget = 0;

	if (RegisterRawInputDevices(Rid, 2, sizeof(Rid[0])) == FALSE) {
		lastError = GetLastError();
	}
}

void handleWindowMessage(UINT message) {
	
}


CaptureInput::~CaptureInput()
{
}

bool CaptureInput::startCapturing(int(*callback)(Event*))
{

	return false;
}

std::list<Event*> CaptureInput::stopCapturing()
{
	return std::list<Event*>();
}
