#include "InjectAndCaptureDll.h"
#include <thread>

iac_dll_error_callback_t c_callback_for_error_reporting = nullptr;
void convert_cpp_error_to_c_error_and_call_callback(const std::string& error_string)
{
	c_callback_for_error_reporting(error_string.c_str());
}

INJECTANDCAPTUREDLL_API void iac_dll_init_with_error_cb(const iac_dll_error_callback_t error_cb)
{
	c_callback_for_error_reporting = error_cb;
	iac_dll::init_with_error_cb(convert_cpp_error_to_c_error_and_call_callback);
}

INJECTANDCAPTUREDLL_API void iac_dll_init() {
	iac_dll::Init();
}

iac_dll_capture_event_cb c_callback_for_event_capture_reporting = nullptr;

void convert_cpp_event_to_c_and_call_callback(const std::unique_ptr<Event> event) {
	auto serialized_event_vec = event->serialize();
	const auto buf_size = serialized_event_vec->size();
	const auto serialized_event_buf = new unsigned char[buf_size];
	memcpy(serialized_event_buf, serialized_event_vec->data(), buf_size);
	c_callback_for_event_capture_reporting(serialized_event_buf, buf_size);
}

INJECTANDCAPTUREDLL_API void iac_dll_start_capture(const iac_dll_capture_event_cb event_capture_cb) {
	c_callback_for_event_capture_reporting = event_capture_cb;
	iac_dll::StartCapture(convert_cpp_event_to_c_and_call_callback);
}

INJECTANDCAPTUREDLL_API void iac_dll_stop_capture() {
	iac_dll::StopCapture();
}

INJECTANDCAPTUREDLL_API void iac_dll_inject_event(const unsigned char serialized_event_buf[], const size_t buf_size) {
	const std::vector<unsigned char> serialized_event(serialized_event_buf,serialized_event_buf+buf_size); //TODO: is this safe? should -1 in the end?
	const auto event = iac_dll::deserialize_event(serialized_event);
	event->inject();
}

INJECTANDCAPTUREDLL_API void iac_dll_inject_events(const unsigned char serialized_event_buf[], const size_t buf_size) {
	const std::vector<unsigned char> serialized_events(serialized_event_buf,serialized_event_buf+buf_size); //TODO: is this safe? should -1 in the end?
	auto events_vec = iac_dll::deserialize_events(serialized_events);

	//TODO: replace with something less stupid:
	std::thread injection_list_thread{
		[events_vec=std::move(events_vec)]{
			const auto start_time = std::chrono::high_resolution_clock::now();
			for (auto&& event : events_vec)
			{
				std::this_thread::sleep_until(start_time + event->time_since_start_of_recording);
				event->inject();
			}
		}
	};
	injection_list_thread.detach();
}