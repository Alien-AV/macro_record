#include "stdafx.h"
#include "WindowForCaptureEvents.h"
#include "InjectAndCaptureDll.h"
#include <Strsafe.h>

WindowForCaptureEvents::WindowForCaptureEvents()
{
}


WindowForCaptureEvents::~WindowForCaptureEvents()
{
}


void RegisterRawInputStuff(HWND hwnd) {
	RAWINPUTDEVICE Rid[2];

	Rid[0].usUsagePage = 0x01;
	Rid[0].usUsage = 0x02;
	Rid[0].dwFlags = RIDEV_INPUTSINK;
	//Rid[0].dwFlags = RIDEV_NOLEGACY;   // adds HID mouse and also ignores legacy mouse messages
	Rid[0].hwndTarget = hwnd;

	Rid[1].usUsagePage = 0x01;
	Rid[1].usUsage = 0x06;
	Rid[1].dwFlags = RIDEV_INPUTSINK;
	//Rid[1].dwFlags = RIDEV_NOLEGACY;   // adds HID keyboard and also ignores legacy keyboard messages
	Rid[1].hwndTarget = hwnd;

	OutputDebugString(TEXT("Registering RawInput Device\n"));

	if (RegisterRawInputDevices(Rid, 2, sizeof(Rid[0])) == FALSE) {
		OutputDebugString(TEXT("Registering RawInput Device failed\n"));
	}
}

HINSTANCE  inj_hModule = NULL;          //Injected Modules Handle
HWND       prnt_hWnd;            //Parent Window Handle
LRESULT CALLBACK DLLWindowProc(HWND, UINT, WPARAM, LPARAM); //WndProc for the new window

#define MYMENU_EXIT         (WM_APP + 101)
#define MYMENU_MESSAGEBOX   (WM_APP + 102) 

BOOL RegisterDLLWindowClass()
{
	WNDCLASSEX wc;
	wc.hInstance = inj_hModule;
	wc.lpszClassName = (LPCWSTR)L"InjectedDLLWindowClass";
	wc.lpfnWndProc = DLLWindowProc;
	wc.style = CS_DBLCLKS;
	wc.cbSize = sizeof(WNDCLASSEX);
	wc.hIcon = LoadIcon(NULL, IDI_APPLICATION);
	wc.hIconSm = LoadIcon(NULL, IDI_APPLICATION);
	wc.hCursor = LoadCursor(NULL, IDC_ARROW);
	wc.lpszMenuName = NULL;
	wc.cbClsExtra = 0;
	wc.cbWndExtra = 0;
	wc.hbrBackground = (HBRUSH)COLOR_BACKGROUND;
	if (!RegisterClassEx(&wc))
		return 0;
}

HMENU CreateDLLWindowMenu()
{
	HMENU hMenu;
	hMenu = CreateMenu();
	HMENU hMenuPopup;
	if (hMenu == NULL)
		return FALSE;
	hMenuPopup = CreatePopupMenu();
	AppendMenu(hMenuPopup, MF_STRING, MYMENU_EXIT, TEXT("Exit"));
	AppendMenu(hMenu, MF_POPUP, (UINT_PTR)hMenuPopup, TEXT("File"));

	hMenuPopup = CreatePopupMenu();
	AppendMenu(hMenuPopup, MF_STRING, MYMENU_MESSAGEBOX, TEXT("MessageBox"));
	AppendMenu(hMenu, MF_POPUP, (UINT_PTR)hMenuPopup, TEXT("Test"));
	return hMenu;
}


DWORD WINAPI ThreadProc(LPVOID lpParam)
{
	MSG messages;
	wchar_t *pString = reinterpret_cast<wchar_t * > (lpParam);
	HMENU hMenu = CreateDLLWindowMenu();
	RegisterDLLWindowClass();
	prnt_hWnd = FindWindow(L"Window Injected Into ClassName", L"Window Injected Into Caption");
	HWND hwnd = CreateWindowEx(0, L"InjectedDLLWindowClass", pString, WS_EX_PALETTEWINDOW, CW_USEDEFAULT, CW_USEDEFAULT, 400, 300, prnt_hWnd, hMenu, inj_hModule, NULL);
	RegisterRawInputStuff(hwnd);
	//ShowWindow(hwnd, SW_SHOWNORMAL);
	while (GetMessage(&messages, NULL, 0, 0))
	{
		TranslateMessage(&messages);
		DispatchMessage(&messages);
	}
	return 1;
}

LRESULT CALLBACK DLLWindowProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
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
				hResult = StringCchPrintf(szTempOutput, STRSAFE_MAX_CCH, TEXT(" Kbd: make=%04x Flags:%04x Reserved:%04x ExtraInformation:%08x, msg=%04x VK=%04x \n"),
					raw->data.keyboard.MakeCode,
					raw->data.keyboard.Flags,
					raw->data.keyboard.Reserved,
					raw->data.keyboard.ExtraInformation,
					raw->data.keyboard.Message,
					raw->data.keyboard.VKey);
				if (FAILED(hResult))
				{
					// TODO: write error handler
				}
				OutputDebugString(szTempOutput);
			}
			else if (raw->header.dwType == RIM_TYPEMOUSE)
			{
				hResult = StringCchPrintf(szTempOutput, STRSAFE_MAX_CCH, TEXT("Mouse: usFlags=%04x ulButtons=%04x usButtonFlags=%04x usButtonData=%04x ulRawButtons=%04x lLastX=%04x lLastY=%04x ulExtraInformation=%04x\r\n"),
					raw->data.mouse.usFlags,
					raw->data.mouse.ulButtons,
					raw->data.mouse.usButtonFlags,
					raw->data.mouse.usButtonData,
					raw->data.mouse.ulRawButtons,
					raw->data.mouse.lLastX,
					raw->data.mouse.lLastY,
					raw->data.mouse.ulExtraInformation);

				if (FAILED(hResult))
				{
					// TODO: write error handler
				}
				OutputDebugString(szTempOutput);
			}

			delete[] lpb;
			return 0;
		}
	case WM_COMMAND:
		switch (wParam)
		{
		case MYMENU_EXIT:
			SendMessage(hwnd, WM_CLOSE, 0, 0);
			break;
		case MYMENU_MESSAGEBOX:
			MessageBox(hwnd, L"Test", L"MessageBox", MB_OK);
			break;
		}
		break;
	case WM_DESTROY:
		PostQuitMessage(0);
		break;
	default:
		return DefWindowProc(hwnd, message, wParam, lParam);
	}
	return 0;
}


INJECTANDCAPTUREDLL_API void MakeWindow(void){
	CreateThread(0, NULL, ThreadProc, (LPVOID)L"Window Title", NULL, NULL);
}