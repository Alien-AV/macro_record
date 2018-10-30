#pragma once

#ifdef RECORD_PLAYBACK_DLL_EXPORTS
#define RECORD_PLAYBACK_DLL_API __declspec(dllexport)
#else
#define RECORD_PLAYBACK_DLL_API __declspec(dllimport)
#endif

#include <Windows.h>

#include "../Common/StatusEnum.cs"

extern "C" {
	using iac_dll_status_cb_t = void(*)(RecordPlaybackDLLEnums::StatusCode status_code);
	using iac_dll_record_event_cb_t = void(*)(const unsigned char buffer[], int buf_size);
	RECORD_PLAYBACK_DLL_API void iac_dll_init(const iac_dll_record_event_cb_t record_event_cb, const iac_dll_status_cb_t status_cb);
	
	RECORD_PLAYBACK_DLL_API void iac_dll_start_record();
	RECORD_PLAYBACK_DLL_API void iac_dll_stop_record();

	RECORD_PLAYBACK_DLL_API void iac_dll_playback_events_abort();
	RECORD_PLAYBACK_DLL_API void iac_dll_playback_event(const unsigned char serialized_event_buf[], size_t buf_size);
	RECORD_PLAYBACK_DLL_API void iac_dll_playback_events(const unsigned char serialized_event_buf[], size_t buf_size);
}