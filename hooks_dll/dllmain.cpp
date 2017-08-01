// dllmain.cpp : Defines the entry point for the DLL application.
#include "stdafx.h"
thread_local std::fstream log_file_stream;

BOOL APIENTRY DllMain( HMODULE hModule,
                       DWORD  ul_reason_for_call,
                       LPVOID lpReserved
					 )
{
	std::stringstream logFileName;
	std::thread::id this_id = std::this_thread::get_id();
	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
		//break;
	case DLL_THREAD_ATTACH:
		logFileName << "C:\\test\\thread" << this_id << ".log";
		log_file_stream.open(logFileName.str(), std::fstream::out | std::fstream::app);
		break;
	case DLL_THREAD_DETACH:
		log_file_stream.close();
		break;
	case DLL_PROCESS_DETACH:
		break;
	}
	return TRUE;
}

