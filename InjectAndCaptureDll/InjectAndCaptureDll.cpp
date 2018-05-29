#include "InjectAndCaptureDll.h"
#include "Common\DeserializeEvent.h"

#include <iostream>
#include <fstream>

INJECTANDCAPTUREDLL_API void iac_dll_init() {
	iac_dll::Init();
}

iac_dll_capture_event_cb c_wrapper_c_api_capture_events_callback = nullptr;

void c_wrapper_cpp_api_capture_events_callback(std::unique_ptr<Event> event) {
	//unwrap event, prvide to c_wrapper_c_capture_events_callback
	auto serialized_event = event->serialize();
	c_wrapper_c_api_capture_events_callback(serialized_event.c_str());
}
INJECTANDCAPTUREDLL_API void iac_dll_start_capture(iac_dll_capture_event_cb cb) {
	c_wrapper_c_api_capture_events_callback = cb;
	iac_dll::StartCapture(c_wrapper_cpp_api_capture_events_callback);
}
INJECTANDCAPTUREDLL_API void iac_dll_stop_capture() {
	iac_dll::StopCapture();
}
INJECTANDCAPTUREDLL_API void iac_dll_inject_event(const char serialized_event[]) {
	auto event = deserializeEvent(serialized_event);
	event->inject();
}
