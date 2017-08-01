// The following ifdef block is the standard way of creating macros which make exporting 
// from a DLL simpler. All files within this DLL are compiled with the HOOKS_DLL_EXPORTS
// symbol defined on the command line. This symbol should not be defined on any project
// that uses this DLL. This way any other project whose source files include this file see 
// HOOKS_DLL_API functions as being imported from a DLL, whereas this DLL sees symbols
// defined with this macro as being exported.
#ifdef HOOKS_DLL_EXPORTS
#define HOOKS_DLL_API __declspec(dllexport)
#else
#define HOOKS_DLL_API __declspec(dllimport)
#endif

extern "C" {
	HOOKS_DLL_API LRESULT CALLBACK LowLevelMouseProc(int nCode, WPARAM wParam, LPARAM lParam);
}