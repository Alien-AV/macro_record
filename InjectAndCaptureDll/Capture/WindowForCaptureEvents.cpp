#include "..\stdafx.h"
#include <memory>
#include <strsafe.h>
#include <algorithm>
#include <chrono>
#include "../InjectAndCaptureDll.h"
#include "../Common/Event.h"
#include "../Common/KeyboardEvent.h"
#include "../Common/MouseEvent.h"

#define WM_STARTCAPTURE (WM_USER+1)
#define WM_STOPCAPTURE (WM_USER+2)

namespace iac_dll {

	CaptureEventsCallback capture_events_callback = nullptr;
	std::chrono::time_point<std::chrono::high_resolution_clock> time_of_start_of_recording;

	DWORD window_thread_id = NULL;


	bool RegisterRawInputStuff(HWND hwnd) {
		RAWINPUTDEVICE Rid[2];

		Rid[0].usUsagePage = 0x01;
		Rid[0].usUsage = 0x02;
		Rid[0].dwFlags = RIDEV_INPUTSINK; // | RIDEV_NOLEGACY;
		Rid[0].hwndTarget = hwnd;

		Rid[1].usUsagePage = 0x01;
		Rid[1].usUsage = 0x06;
		Rid[1].dwFlags = RIDEV_INPUTSINK; // | RIDEV_NOLEGACY;
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
		Rid[0].hwndTarget = nullptr;

		Rid[1].usUsagePage = 0x01;
		Rid[1].usUsage = 0x06;
		Rid[1].dwFlags = RIDEV_REMOVE;
		Rid[1].hwndTarget = nullptr;

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
		fakeMouseEvent->time_since_start_of_recording = std::chrono::microseconds(0);
		fakeMouseEvent->x = initialMousePosition.x;
		fakeMouseEvent->y = initialMousePosition.y;
		fakeMouseEvent->ActionType = MouseEvent::ActionTypeFlags::Move;
		capture_events_callback(std::move(fakeMouseEvent));
	}

	DWORD WINAPI CaptureWindowMainLoopThread(LPVOID lpParam)
	{
		MSG messages;

		HWND hwnd = nullptr;

		const LPCWSTR class_name = L"INJECT_AND_CAPTURE_DLL_WINDOW_CLASS";
		WNDCLASSEX wx = {};
		wx.cbSize = sizeof(WNDCLASSEX);
		wx.lpfnWndProc = CaptureWindowWndProc;
		wx.lpszClassName = class_name;

		if (RegisterClassEx(&wx)) {
			hwnd = CreateWindowEx(0, class_name, L"inject_and_capture_dll", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, nullptr, nullptr);
			if (!hwnd) {
				return 1;
			}
		}

		while (GetMessage(&messages, nullptr, 0, 0))
		{
			switch (messages.message) {
			case WM_STARTCAPTURE:
				time_of_start_of_recording = std::chrono::high_resolution_clock::now();
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
		const auto time_since_start_of_recording = std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::high_resolution_clock::now() - time_of_start_of_recording);
		auto captured_kbd_event = std::make_unique<KeyboardEvent>();
		captured_kbd_event->time_since_start_of_recording = time_since_start_of_recording;
		captured_kbd_event->virtualKeyCode = data.VKey;
		if (data.Flags & RI_KEY_BREAK) {
			captured_kbd_event->keyUp = true;
		}
		else {
			captured_kbd_event->keyUp = false;
		}

		capture_events_callback(std::move(captured_kbd_event));
	}

	void HandleMouseEventCapture(RAWMOUSE data) {
		const auto time_since_start_of_recording = std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::high_resolution_clock::now() - time_of_start_of_recording);

		auto captured_mouse_event = std::make_unique<MouseEvent>();
		captured_mouse_event->time_since_start_of_recording = time_since_start_of_recording;
		captured_mouse_event->mappedToVirtualDesktop = (data.usFlags & MOUSE_VIRTUAL_DESKTOP);

		captured_mouse_event->relative_position = !(data.usFlags & MOUSE_MOVE_ABSOLUTE);

		captured_mouse_event->x = data.lLastX;
		captured_mouse_event->y = data.lLastY;

		captured_mouse_event->ActionType |= MouseEvent::ActionTypeFlags::Move;

		if (data.usButtonFlags & RI_MOUSE_WHEEL) {
			captured_mouse_event->wheelRotation = data.usButtonData;
		}

		if (data.usButtonFlags & RI_MOUSE_LEFT_BUTTON_DOWN) {
			captured_mouse_event->ActionType |= MouseEvent::ActionTypeFlags::LeftDown;
		}

		if (data.usButtonFlags & RI_MOUSE_LEFT_BUTTON_UP) {
			captured_mouse_event->ActionType |= MouseEvent::ActionTypeFlags::LeftUp;
		}
		if (data.usButtonFlags & RI_MOUSE_MIDDLE_BUTTON_DOWN) {
			captured_mouse_event->ActionType |= MouseEvent::ActionTypeFlags::MiddleDown;
		}

		if (data.usButtonFlags & RI_MOUSE_MIDDLE_BUTTON_UP) {
			captured_mouse_event->ActionType |= MouseEvent::ActionTypeFlags::MiddleUp;
		}

		if (data.usButtonFlags & RI_MOUSE_RIGHT_BUTTON_DOWN) {
			captured_mouse_event->ActionType |= MouseEvent::ActionTypeFlags::RightDown;
		}

		if (data.usButtonFlags & RI_MOUSE_RIGHT_BUTTON_UP) {
			captured_mouse_event->ActionType |= MouseEvent::ActionTypeFlags::RightUp;
		}

		if (data.usButtonFlags & RI_MOUSE_BUTTON_4_DOWN) {
			captured_mouse_event->ActionType |= MouseEvent::ActionTypeFlags::XDown;
		}

		if (data.usButtonFlags & RI_MOUSE_BUTTON_4_UP) {
			captured_mouse_event->ActionType |= MouseEvent::ActionTypeFlags::XUp;
		}

		//TODO: x2 button

		capture_events_callback(std::move(captured_mouse_event));
	}

	LRESULT CALLBACK CaptureWindowWndProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
	{
		switch (message)
		{
		case WM_INPUT:
		{
			UINT dwSize;
			GetRawInputData(HRAWINPUT(lParam), RID_INPUT, nullptr, &dwSize,
				sizeof(RAWINPUTHEADER));
			const auto lpb = new BYTE[dwSize];
			if (lpb == nullptr)
			{
				return 0;
			}

			if (GetRawInputData(HRAWINPUT(lParam), RID_INPUT, lpb, &dwSize,
				sizeof(RAWINPUTHEADER)) != dwSize)
				OutputDebugString(TEXT("GetRawInputData does not return correct size !\n"));

			const auto raw = reinterpret_cast<RAWINPUT*>(lpb);

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


	INJECTANDCAPTUREDLL_API void Init() {
		if (window_thread_id) {
			OutputDebugString(L"Init already called");
			return;
		}
		CreateThread(nullptr, NULL, CaptureWindowMainLoopThread, LPVOID(L"Window Title"), NULL, &window_thread_id);
	}

	INJECTANDCAPTUREDLL_API BOOL StartCapture(CaptureEventsCallback newCaptureEventsCallback) {
		capture_events_callback = newCaptureEventsCallback;
		return PostThreadMessage(window_thread_id, WM_STARTCAPTURE, NULL, NULL);
	}

	INJECTANDCAPTUREDLL_API BOOL StopCapture() {
		return PostThreadMessage(window_thread_id, WM_STOPCAPTURE, NULL, NULL);
	}

}