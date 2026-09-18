#pragma once

#ifdef RECORD_PLAYBACK_DLL_EXPORTS
#define RECORD_PLAYBACK_DLL_API __declspec(dllexport)
#else
#define RECORD_PLAYBACK_DLL_API __declspec(dllimport)
#endif

#include <Windows.h>
#include <cstdint>

#include "../Common/StatusEnum.cs"

enum class PlaybackResult : int {
	Running, Finished, Cancelled, InvalidInput, Busy, InjectionFailed, InternalError, StaleSession
};

extern "C" {
	using iac_dll_status_cb_t = void(*)(RecordPlaybackDLLEnums::StatusCode status_code);
	// Borrowed buffer, valid only until the callback returns. Consumers must copy.
	using iac_dll_record_event_cb_t = void(*)(const unsigned char buffer[], int buf_size, uint64_t session_id);
	using iac_dll_record_boundary_cb_t = void(*)(uint64_t session_id, uint32_t boundary, uint32_t held_keys);
	RECORD_PLAYBACK_DLL_API bool iac_dll_init(iac_dll_record_event_cb_t, iac_dll_status_cb_t, iac_dll_record_boundary_cb_t) noexcept;
	
	RECORD_PLAYBACK_DLL_API bool iac_dll_start_record(uint64_t session_id) noexcept;
	RECORD_PLAYBACK_DLL_API bool iac_dll_stop_record(uint64_t session_id) noexcept;
	RECORD_PLAYBACK_DLL_API void iac_dll_record_shutdown() noexcept;

	// Input is copied before returning. Empty/malformed lists are rejected without injection.
	RECORD_PLAYBACK_DLL_API PlaybackResult iac_dll_playback_start(const unsigned char buffer[], size_t size, int loop, uint64_t* session_id) noexcept;
	RECORD_PLAYBACK_DLL_API PlaybackResult iac_dll_playback_poll(uint64_t session_id) noexcept;
	RECORD_PLAYBACK_DLL_API PlaybackResult iac_dll_playback_set_loop(uint64_t session_id, int loop) noexcept;
	// Abort joins the worker, including best-effort releases of held synthetic input.
	RECORD_PLAYBACK_DLL_API PlaybackResult iac_dll_playback_abort(uint64_t session_id) noexcept;
	RECORD_PLAYBACK_DLL_API void iac_dll_playback_shutdown() noexcept;
}
