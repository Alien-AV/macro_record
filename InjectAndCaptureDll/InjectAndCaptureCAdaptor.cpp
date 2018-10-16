#include "InjectAndCaptureDll.h"
#include <thread>
#include "Capture/CaptureEngine.h"
#include "Common/DeserializeEvent.h"

iac_dll_status_cb_t c_callback_for_status_reporting = nullptr;
iac_dll_capture_event_cb_t c_callback_for_event_capture_reporting = nullptr;
std::unique_ptr<iac_dll::CaptureEngine> capture_engine_singleton;

void convert_cpp_status_to_c_status_and_call_callback(const InjectAndCaptureDllEnums::StatusCode status_code)
{
	c_callback_for_status_reporting(status_code);
}

void convert_cpp_event_to_c_and_call_callback(const std::unique_ptr<Event> event) {
	auto serialized_event_vec = event->serialize();
	const auto buf_size = serialized_event_vec->size();
	const auto serialized_event_buf = new unsigned char[buf_size]; //TODO: what frees it?
	memcpy(serialized_event_buf, serialized_event_vec->data(), buf_size);
	c_callback_for_event_capture_reporting(serialized_event_buf, buf_size);
}

INJECTANDCAPTUREDLL_API void iac_dll_init(iac_dll_capture_event_cb_t event_capture_cb, iac_dll_status_cb_t status_cb)
{
	c_callback_for_event_capture_reporting = event_capture_cb;
	c_callback_for_status_reporting = status_cb;
	capture_engine_singleton = std::make_unique<iac_dll::CaptureEngine>(convert_cpp_event_to_c_and_call_callback, convert_cpp_status_to_c_status_and_call_callback);
}

INJECTANDCAPTUREDLL_API void iac_dll_start_capture() {
	capture_engine_singleton->start_capture();
}

INJECTANDCAPTUREDLL_API void iac_dll_stop_capture() {
	capture_engine_singleton->stop_capture();
}

INJECTANDCAPTUREDLL_API void iac_dll_inject_event(const unsigned char serialized_event_buf[], const size_t buf_size) {
	const std::vector<unsigned char> serialized_event(serialized_event_buf,serialized_event_buf+buf_size); //TODO: is this safe? should -1 in the end?
	const auto event = iac_dll::deserialize_event(serialized_event);
	event->inject();
}

bool stop_injection;

INJECTANDCAPTUREDLL_API void iac_dll_inject_events_abort()
{
	stop_injection = true;
}

INJECTANDCAPTUREDLL_API void iac_dll_inject_events(const unsigned char serialized_event_buf[], const size_t buf_size) {
	const std::vector<unsigned char> serialized_events(serialized_event_buf,serialized_event_buf+buf_size); //TODO: is this safe? should -1 in the end?
	auto events_vec = iac_dll::deserialize_events(serialized_events);

	//TODO: should this logic be in the c adaptor layer?
	std::thread injection_list_thread{
		[events_vec=std::move(events_vec)]{
			stop_injection = false;

			const auto start_time = std::chrono::high_resolution_clock::now();
			for (auto&& event : events_vec)
			{
				if(stop_injection)
				{
					return;
				}
				std::this_thread::sleep_until(start_time + event->time_since_start_of_recording);
				event->inject();
			}
			c_callback_for_status_reporting(InjectAndCaptureDllEnums::PlaybackFinished); // <- ye this is pretty dumb
		}
	};
	injection_list_thread.detach();
}