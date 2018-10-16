#pragma once

#ifdef INJECTANDCAPTUREDLL_EXPORTS
#define INJECTANDCAPTUREDLL_API __declspec(dllexport)
#else
#define INJECTANDCAPTUREDLL_API __declspec(dllimport)
#endif

#include "Common/Event.h"
#include <memory>
#include <Windows.h>
#include <vector>

#include "../Common/StatusEnum.cs"

namespace iac_dll {
	// INJECTANDCAPTUREDLL_API void HandleMouseEventCapture(RAWMOUSE data); //TODO: exporting for tests - and I don't like it - isn't there other way?
 //
	// using status_callback_t = void(*)(const std::string& error);
	// typedef void(*capture_events_callback_t)(std::unique_ptr<Event>);
	// INJECTANDCAPTUREDLL_API void Init();
 //
	// INJECTANDCAPTUREDLL_API BOOL StartCapture(capture_events_callback_t);
	// INJECTANDCAPTUREDLL_API BOOL StopCapture();
	// //TODO: need to add "InjectEvent" export?
	INJECTANDCAPTUREDLL_API std::ostream &operator<<(std::ostream &outstream, Event const &event);
	INJECTANDCAPTUREDLL_API std::unique_ptr<Event> deserialize_event(std::vector<unsigned char>);
	INJECTANDCAPTUREDLL_API std::vector<std::unique_ptr<Event>> deserialize_events(std::vector<unsigned char>);
}

extern "C" {
	using iac_dll_status_cb_t = void(*)(InjectAndCaptureDllEnums::StatusCode status_code);
	using iac_dll_capture_event_cb_t = void(*)(const unsigned char buffer[], int buf_size);
	INJECTANDCAPTUREDLL_API void iac_dll_init(const iac_dll_capture_event_cb_t event_capture_cb, const iac_dll_status_cb_t status_cb);
	
	INJECTANDCAPTUREDLL_API void iac_dll_start_capture();
	INJECTANDCAPTUREDLL_API void iac_dll_stop_capture();

	INJECTANDCAPTUREDLL_API void iac_dll_inject_events_abort();
	INJECTANDCAPTUREDLL_API void iac_dll_inject_event(const unsigned char serialized_event_buf[], size_t buf_size);
	INJECTANDCAPTUREDLL_API void iac_dll_inject_events(const unsigned char serialized_event_buf[], size_t buf_size);
}