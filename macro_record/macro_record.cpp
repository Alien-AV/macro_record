// macro_record.cpp : Defines the entry point for the console application.
//

#include "stdafx.h"
#include <fstream>
#include "InjectInput.h"

int main()
{
	//hookStuff();
	auto inputInjector = new InjectInput();
	KeyboardEvent ke;
	
	ke.virtualKeyCode = VK_RETURN; // 'q'
	ke.keyUp = false;
	Sleep(3000);
	if (inputInjector->InjectEvent(ke) == false) {
		std::cout << "omg failure: " << inputInjector->lastError << std::endl;
		char c;
		std::cin >> c;
	}

	ke.virtualKeyCode = VK_RETURN;
	ke.keyUp = true;
	Sleep(100);
	inputInjector->InjectEvent(ke);


	MouseEvent me;
	me.ActionType = MouseEvent::ActionTypeFlag::LeftDown;
	
    return 0;
}

void hookStuff() {
	HOOKPROC LowLevelMouseProc;
	static HINSTANCE hinstDLL;
	static HHOOK hhookLowLevelMouse;

	hinstDLL = LoadLibrary(TEXT("hooks_dll.dll"));
	std::cout << "LoadLibrary result: " << hinstDLL << ", GetLastError: " << GetLastError() << std::endl;
	LowLevelMouseProc = (HOOKPROC)GetProcAddress(hinstDLL, "LowLevelMouseProc");
	std::cout << "GetProcAddress result: " << LowLevelMouseProc << ", GetLastError: " << GetLastError() << std::endl;

	if (GetLastError() != 0 || hinstDLL == nullptr || LowLevelMouseProc == nullptr) {
		char c;
		std::cin >> c;
		return;
	}

	hhookLowLevelMouse = SetWindowsHookEx(WH_MOUSE, LowLevelMouseProc, hinstDLL, 0);
	Sleep(5000);

	UnhookWindowsHookEx(hhookLowLevelMouse);
}
