#include "WindowForCaptureEvents.h"
#include "../InjectAndCaptureDll.h"
#include "../Common/Event.h"
#include "../Common/KeyboardEvent.h"
#include "../Common/MouseEvent.h"
#include <Strsafe.h>
#include <algorithm>
#include <iostream>
#include <list>
//#include <chrono>
#include "../Common/IdleEvent.h"

#define WM_STARTCAPTURE WM_USER+1
#define WM_STOPCAPTURE WM_USER+2

HANDLE canCollectResult = NULL;

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

std::list<Event*> *globalEventList;

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

	//RegisterRawInputStuff(hwnd);

	//ShowWindow(hwnd, SW_SHOWNORMAL);
	while (GetMessage(&messages, NULL, 0, 0))
	{
		switch (messages.message) {
		case WM_STARTCAPTURE:
			RegisterRawInputStuff(hwnd);
			break;
		case WM_STOPCAPTURE:
			UnregisterRawInputStuff();
			if (!SetEvent(canCollectResult))
			{
				printf("SetEvent failed (%d)\n", GetLastError());
				return 1;
			}
			break;
		default:
			TranslateMessage(&messages);
			DispatchMessage(&messages);
		}
	}
	return 1;
}

void HandleKeyboardEventCapture(RAWKEYBOARD data) {
	HRESULT hResult;

	auto idleEndTime = std::chrono::high_resolution_clock::now();

	IdleEvent *idleEvent = new IdleEvent(idleEndTime-idleStartTime);
	globalEventList->push_back(idleEvent);

	KeyboardEvent *capturedKbdEvent = new KeyboardEvent();
	capturedKbdEvent->virtualKeyCode = data.VKey;
	if (data.Flags & RI_KEY_BREAK) {
		capturedKbdEvent->keyUp = true;
	}else if (data.Flags & RI_KEY_MAKE) {
		capturedKbdEvent->keyUp = false;
	}
	else {
		return;
	}

	// stop timer and capture timing data // need for every event
	// fill keybd event
	// provide keybd event to client
	// start timer


	globalEventList->push_back(capturedKbdEvent);

	idleStartTime = std::chrono::high_resolution_clock::now();

	//TCHAR szTempOutput[1024];
	//hResult = StringCchPrintf(szTempOutput, STRSAFE_MAX_CCH, TEXT(" Kbd: make=%04x Flags:%04x Reserved:%04x ExtraInformation:%08x, msg=%04x VK=%04x \n"),
	//	data.MakeCode,
	//	data.Flags,
	//	data.Reserved,
	//	data.ExtraInformation,
	//	data.Message,
	//	data.VKey);
	//if (FAILED(hResult))
	//{
	//	// TODO: write error handler
	//}
	//OutputDebugString(szTempOutput);
}

void HandleMouseEventCapture(RAWMOUSE data) {
	HRESULT hResult;
	TCHAR szTempOutput[1024];
	hResult = StringCchPrintf(szTempOutput, STRSAFE_MAX_CCH, TEXT("Mouse: usFlags=%04x ulButtons=%04x usButtonFlags=%04x usButtonData=%04x ulRawButtons=%04x lLastX=%04x lLastY=%04x ulExtraInformation=%04x\r\n"),
		data.usFlags,
		data.ulButtons,
		data.usButtonFlags,
		data.usButtonData,
		data.ulRawButtons,
		data.lLastX,
		data.lLastY,
		data.ulExtraInformation);

	if (FAILED(hResult))
	{
		// TODO: write error handler
	}
	OutputDebugString(szTempOutput);
}

LRESULT CALLBACK CaptureWindowWndProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
{
	switch (message)
	{
	case WM_INPUT:
	{
		UINT dwSize;
		HRESULT hResult;
		TCHAR szTempOutput[1024];
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
	canCollectResult = CreateEvent(NULL, FALSE, FALSE, TEXT("WriteEvent"));
	CreateThread(0, NULL, CaptureWindowMainLoopThread, (LPVOID)L"Window Title", NULL, &window_thread_id);
}

INJECTANDCAPTUREDLL_API BOOL StartCapture(CaptureEventsCallback newCaptureEventsCallback) {
	//globalEventList = new std::list<Event*>;
	captureEventsCallback = newCaptureEventsCallback;
	return PostThreadMessage(window_thread_id, WM_STARTCAPTURE, NULL, NULL);
}

INJECTANDCAPTUREDLL_API std::list<Event*>* StopCapture() {
	if (!PostThreadMessage(window_thread_id, WM_STOPCAPTURE, NULL, NULL)) {
		return NULL;
	}
	DWORD dwWaitResult = WaitForSingleObject(canCollectResult, INFINITE);
	switch (dwWaitResult)
	{
	case WAIT_OBJECT_0:
		return globalEventList;
		break;
	default:
		printf("Wait error (%d)\n", GetLastError());
		return NULL;
	}
}