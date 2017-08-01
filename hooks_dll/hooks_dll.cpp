// hooks_dll.cpp : Defines the exported functions for the DLL application.
//

#include "stdafx.h"
#include "hooks_dll.h"

// This is an example of an exported function.
extern "C" HOOKS_DLL_API LRESULT CALLBACK LowLevelMouseProc(int nCode, WPARAM wParam, LPARAM lParam)
{
	#pragma comment(linker, "/EXPORT:" __FUNCTION__ "=" __FUNCDNAME__)
	if (log_file_stream.is_open()) {
		PMOUSEHOOKSTRUCT mousehookst = (PMOUSEHOOKSTRUCT)lParam;

		log_file_stream << "wParam: " << wParam << ", coords: " << mousehookst->pt.x << "," << mousehookst->pt.y << std::endl;
	}
	return CallNextHookEx(NULL, nCode, wParam, lParam);
}