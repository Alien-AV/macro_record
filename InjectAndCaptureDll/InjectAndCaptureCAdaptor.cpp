#include "InjectAndCaptureDll.h"

INJECTANDCAPTUREDLL_API void iac_dll_init() {
	iac_dll::Init();
}

iac_dll_capture_event_cb capture_events_actual_c_callback = nullptr;

void capture_events_cpp_to_c_proxy_cb(std::unique_ptr<Event> event) {
	auto serialized_event_vec = event->serialize();
	const auto buf_size = serialized_event_vec->size();
	const auto serialized_event_buf = new unsigned char[buf_size];
	memcpy(serialized_event_buf, serialized_event_vec->data(), buf_size);
	capture_events_actual_c_callback(serialized_event_buf, buf_size);
}
INJECTANDCAPTUREDLL_API void iac_dll_start_capture(iac_dll_capture_event_cb cb) {
	capture_events_actual_c_callback = cb;
	iac_dll::StartCapture(capture_events_cpp_to_c_proxy_cb);
}
INJECTANDCAPTUREDLL_API void iac_dll_stop_capture() {
	iac_dll::StopCapture();
}
INJECTANDCAPTUREDLL_API void iac_dll_inject_event(const unsigned char serialized_event_buf[], const size_t buf_size) {
	const std::vector<unsigned char> serialized_event(serialized_event_buf,serialized_event_buf+buf_size); //TODO: is this safe? should -1 in the end?
	const auto event = iac_dll::deserialize_event(serialized_event);
	event->inject();
}
