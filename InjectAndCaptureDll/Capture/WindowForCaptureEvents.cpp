#include "..\stdafx.h"
#include <memory>
#include <Strsafe.h>
#include <algorithm>
#include <iostream>
#include <list>
#include <chrono>
#include "WindowForCaptureEvents.h"
#include "../InjectAndCaptureDll.h"
#include "../Common/Event.h"
#include "../Common/KeyboardEvent.h"
#include "../Common/MouseEvent.h"

#define WM_STARTCAPTURE WM_USER+1
#define WM_STOPCAPTURE WM_USER+2

CaptureEventsCallback captureEventsCallback = nullptr;
std::chrono::time_point<std::chrono::high_resolution_clock> idleStartTime;

WindowForCaptureEvents::WindowForCaptureEvents()
{
}


WindowForCaptureEvents::~WindowForCaptureEvents()
{
}

DWORD window_thread_id = NULL;


bool RegisterRawInputStuff(HWND hwnd) {
	RAWINPUTDEVICE Rid[2];

	Rid[0].usUsagePage = 0x01;
	Rid[0].usUsage = 0x02;
	Rid[0].dwFlags = RIDEV_INPUTSINK | RIDEV_NOLEGACY;
	Rid[0].hwndTarget = hwnd;

	Rid[1].usUsagePage = 0x01;
	Rid[1].usUsage = 0x06;
	Rid[1].dwFlags = RIDEV_INPUTSINK | RIDEV_NOLEGACY;
	Rid[1].hwndTarget = hwnd;

	OutputDebugString(TEXT("Registering RawInput Device\n"));

	if (RegisterRawInputDevices(Rid, 2, sizeof(Rid[0])) == FALSE) {
		OutputDebugString(TEXT("Registering RawInput Device failed\n"));
		return false;
	}
	return true;
}

bool UnregisterRawInputStuff() {
	RAWINPUTDEVICE Rid[2];

	Rid[0].usUsagePage = 0x01;
	Rid[0].usUsage = 0x02;
	Rid[0].dwFlags = RIDEV_REMOVE;
	Rid[0].hwndTarget = NULL;

	Rid[1].usUsagePage = 0x01;
	Rid[1].usUsage = 0x06;
	Rid[1].dwFlags = RIDEV_REMOVE;
	Rid[1].hwndTarget = NULL;

	OutputDebugString(TEXT("Unregistering RawInput Device\n"));

	if (RegisterRawInputDevices(Rid, 2, sizeof(Rid[0])) == FALSE) {
		OutputDebugString(TEXT("Unregistering RawInput Device failed\n"));
		return false;
	}
	return true;
}

LRESULT CALLBACK CaptureWindowWndProc(HWND, UINT, WPARAM, LPARAM); //WndProc for the new window

void fakeMouseEventForInitialPos() {
	POINT initialMousePosition;
	GetCursorPos(&initialMousePosition);

	auto fakeMouseEvent = std::make_unique<MouseEvent>();
	fakeMouseEvent->idleDurationBefore = std::chrono::nanoseconds(0);
	fakeMouseEvent->x = initialMousePosition.x;
	fakeMouseEvent->y = initialMousePosition.y;
	fakeMouseEvent->ActionType = MouseEvent::ActionTypeFlag::Move;
	captureEventsCallback(std::move(fakeMouseEvent));
}

DWORD WINAPI CaptureWindowMainLoopThread(LPVOID lpParam)
{
	MSG messages;
	wchar_t *pString = reinterpret_cast<wchar_t *> (lpParam);

	HWND hwnd = NULL;

	LPCWSTR class_name = L"INJECT_AND_CAPTURE_DLL_WINDOW_CLASS";
	WNDCLASSEX wx = {};
	wx.cbSize = sizeof(WNDCLASSEX);
	wx.lpfnWndProc = CaptureWindowWndProc;        // function which will handle messages
	wx.lpszClassName = class_name;

	if (RegisterClassEx(&wx)) {
		hwnd = CreateWindowEx(0, class_name, L"inject_and_capture_dll", 0, 0, 0, 0, 0, HWND_MESSAGE, NULL, NULL, NULL);
		if (!hwnd) {
			return 1;
		}
	}

	while (GetMessage(&messages, NULL, 0, 0))
	{
		switch (messages.message) {
		case WM_STARTCAPTURE:
			idleStartTime = std::chrono::high_resolution_clock::now();
			fakeMouseEventForInitialPos();

			RegisterRawInputStuff(hwnd);
			break;
		case WM_STOPCAPTURE:
			UnregisterRawInputStuff();
			break;
		default:
			TranslateMessage(&messages);
			DispatchMessage(&messages);
		}
	}
	return 1;
}

void HandleKeyboardEventCapture(RAWKEYBOARD data) {
	auto idleEndTime = std::chrono::high_resolution_clock::now();

	auto capturedKbdEvent = std::make_unique<KeyboardEvent>();
	capturedKbdEvent->idleDurationBefore = idleEndTime - idleStartTime;
	capturedKbdEvent->virtualKeyCode = data.VKey;
	if (data.Flags & RI_KEY_BREAK) {
		capturedKbdEvent->keyUp = true;
	}
	else {
		capturedKbdEvent->keyUp = false;
	}

	captureEventsCallback(std::move(capturedKbdEvent));

	idleStartTime = std::chrono::high_resolution_clock::now();
}

void HandleMouseEventCapture(RAWMOUSE data) {
	auto idleEndTime = std::chrono::high_resolution_clock::now();

	auto capturedMouseEvent = std::make_unique<MouseEvent>();
	capturedMouseEvent->idleDurationBefore = idleEndTime - idleStartTime;
	capturedMouseEvent->useRelativePosition = !(data.usFlags & MOUSE_MOVE_ABSOLUTE);
	capturedMouseEvent->mappedToVirtualDesktop = (data.usFlags & MOUSE_VIRTUAL_DESKTOP);
	capturedMouseEvent->x = data.lLastX;
	capturedMouseEvent->y = data.lLastY;

	capturedMouseEvent->ActionType |= MouseEvent::ActionTypeFlag::Move;
	
	if (data.usButtonFlags & RI_MOUSE_WHEEL) {
		capturedMouseEvent->wheelRotation = data.usButtonData;
	}

	if (data.usButtonFlags & RI_MOUSE_LEFT_BUTTON_DOWN) {
		capturedMouseEvent->ActionType |= MouseEvent::ActionTypeFlag::LeftDown;
	}
	
	if (data.usButtonFlags & RI_MOUSE_LEFT_BUTTON_UP) {
		capturedMouseEvent->ActionType |= MouseEvent::ActionTypeFlag::LeftUp;
	}
	if (data.usButtonFlags & RI_MOUSE_MIDDLE_BUTTON_DOWN) {
		capturedMouseEvent->ActionType |= MouseEvent::ActionTypeFlag::MiddleDown;
	}
	
	if (data.usButtonFlags & RI_MOUSE_MIDDLE_BUTTON_UP) {
		capturedMouseEvent->ActionType |= MouseEvent::ActionTypeFlag::MiddleUp;
	}

	if (data.usButtonFlags & RI_MOUSE_RIGHT_BUTTON_DOWN) {
		capturedMouseEvent->ActionType |= MouseEvent::ActionTypeFlag::RightDown;
	}
	
	if (data.usButtonFlags & RI_MOUSE_RIGHT_BUTTON_UP) {
		capturedMouseEvent->ActionType |= MouseEvent::ActionTypeFlag::RightUp;
	}

	if (data.usButtonFlags & RI_MOUSE_BUTTON_4_DOWN) {
		capturedMouseEvent->ActionType |= MouseEvent::ActionTypeFlag::XDown;
	}

	if (data.usButtonFlags & RI_MOUSE_BUTTON_4_UP) {
		capturedMouseEvent->ActionType |= MouseEvent::ActionTypeFlag::XUp;
	}

	//TODO: x2 button
	
	captureEventsCallback(std::move(capturedMouseEvent));

	idleStartTime = std::chrono::high_resolution_clock::now();
}

LRESULT CALLBACK CaptureWindowWndProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
{
	switch (message)
	{
	case WM_INPUT:
	{
		UINT dwSize;
		GetRawInputData((HRAWINPUT)lParam, RID_INPUT, NULL, &dwSize,
			sizeof(RAWINPUTHEADER));
		LPBYTE lpb = new BYTE[dwSize];
		if (lpb == NULL)
		{
			return 0;
		}

		if (GetRawInputData((HRAWINPUT)lParam, RID_INPUT, lpb, &dwSize,
			sizeof(RAWINPUTHEADER)) != dwSize)
			OutputDebugString(TEXT("GetRawInputData does not return correct size !\n"));

		RAWINPUT* raw = (RAWINPUT*)lpb;

		if (raw->header.dwType == RIM_TYPEKEYBOARD)
		{
			HandleKeyboardEventCapture(raw->data.keyboard);
		}
		else if (raw->header.dwType == RIM_TYPEMOUSE)
		{
			HandleMouseEventCapture(raw->data.mouse);
		}

		delete[] lpb;
		return 0;
	}
	case WM_DESTROY:
		PostQuitMessage(0);
		break;
	default:
		return DefWindowProc(hwnd, message, wParam, lParam);
	}
	return 0;
}


INJECTANDCAPTUREDLL_API void Init(void) {
	if (window_thread_id) {
		OutputDebugString(L"Init already called");
		return;
	}
	CreateThread(0, NULL, CaptureWindowMainLoopThread, (LPVOID)L"Window Title", NULL, &window_thread_id);
}

INJECTANDCAPTUREDLL_API BOOL StartCapture(CaptureEventsCallback newCaptureEventsCallback) {
	captureEventsCallback = newCaptureEventsCallback;
	return PostThreadMessage(window_thread_id, WM_STARTCAPTURE, NULL, NULL);
}

INJECTANDCAPTUREDLL_API BOOL StopCapture() {
	return PostThreadMessage(window_thread_id, WM_STOPCAPTURE, NULL, NULL);
}