// dllmain.cpp : Defines the entry point for the DLL application.
#include "stdafx.h"
#include "InjectAndCaptureDll.h"
#include "Capture/CaptureEngine.h"

BOOL APIENTRY DllMain( HMODULE hModule,
                       DWORD  ul_reason_for_call,
                       LPVOID lpReserved
					 )
{
	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
	case DLL_THREAD_ATTACH:
	case DLL_THREAD_DETACH:
	case DLL_PROCESS_DETACH:
		break;
	default:
		break;
	}
	return TRUE;
}


namespace iac_dll
{
	error_callback_t error_callback = nullptr; //TODO: make this a singleton
	INJECTANDCAPTUREDLL_API void Init()
	{
		InitCapture();
	}


	INJECTANDCAPTUREDLL_API void init_with_error_cb(const error_callback_t callback)
	{
		error_callback = callback;
		InitCapture();
	}
}