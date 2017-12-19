// dllmain.cpp : Defines the entry point for the DLL application.
#include "stdafx.h"
#include "InjectInput.h"
#include "WindowForCaptureEvents.h"

BOOL APIENTRY DllMain( HMODULE hModule,
                       DWORD  ul_reason_for_call,
                       LPVOID lpReserved
					 )
{
	//auto inputInjector = new InjectInput();
	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
		//inj_hModule = hModule;
		break;
	case DLL_THREAD_ATTACH:
	case DLL_THREAD_DETACH:
	case DLL_PROCESS_DETACH:
		break;
	}
	return TRUE;
}

