#include "RecordPlaybackDLL.h"
#include <thread>
#include "Record/RecordEngine.h"
#include "Common/DeserializeEvent.h"

using namespace std::chrono_literals;

iac_dll_status_cb_t c_callback_for_status_reporting = nullptr;
iac_dll_record_event_cb_t c_callback_for_record_event_reporting = nullptr;
std::unique_ptr<record_playback::RecordEngine> record_engine_singleton;

void convert_cpp_status_to_c_status_and_call_callback(const RecordPlaybackDLLEnums::StatusCode status_code)
{
	c_callback_for_status_reporting(status_code);
}

void convert_cpp_event_to_c_and_call_callback(const std::unique_ptr<Event> event) {
	auto serialized_event_vec = event->serialize();
	const auto buf_size = serialized_event_vec->size();
	const auto serialized_event_buf = new unsigned char[buf_size]; //TODO: what frees it?
	memcpy(serialized_event_buf, serialized_event_vec->data(), buf_size);
	c_callback_for_record_event_reporting(serialized_event_buf, buf_size);
}

RECORD_PLAYBACK_DLL_API void iac_dll_init(iac_dll_record_event_cb_t event_record_cb, iac_dll_status_cb_t status_cb)
{
	c_callback_for_record_event_reporting = event_record_cb;
	c_callback_for_status_reporting = status_cb;
	record_engine_singleton = std::make_unique<record_playback::RecordEngine>(convert_cpp_event_to_c_and_call_callback, convert_cpp_status_to_c_status_and_call_callback);
}

RECORD_PLAYBACK_DLL_API void iac_dll_start_record() {
	record_engine_singleton->start_record();
}

RECORD_PLAYBACK_DLL_API void iac_dll_stop_record() {
	record_engine_singleton->stop_record();
}

RECORD_PLAYBACK_DLL_API void iac_dll_playback_event(const unsigned char serialized_event_buf[], const size_t buf_size) {
	const std::vector<unsigned char> serialized_event(serialized_event_buf,serialized_event_buf+buf_size); //TODO: is this safe? should -1 in the end?
	const auto event = record_playback::deserialize_event(serialized_event);
	event->playback();
}

bool stop_playback;

RECORD_PLAYBACK_DLL_API void iac_dll_playback_events_abort()
{
	stop_playback = true;
}

RECORD_PLAYBACK_DLL_API void iac_dll_playback_events(const unsigned char serialized_event_buf[], const size_t buf_size) {
	const std::vector<unsigned char> serialized_events(serialized_event_buf,serialized_event_buf+buf_size); //TODO: is this safe? should -1 in the end?
	auto events_vec = record_playback::deserialize_events(serialized_events);

	//TODO: should this logic be in the c adaptor layer?
	std::thread playback_list_thread{
		[events_vec=std::move(events_vec)]{
			stop_playback = false;

			const auto start_time = std::chrono::high_resolution_clock::now();
			std::chrono::microseconds sum_of_all_event_delays_till_now = 0ms;
			for (auto&& event : events_vec)
			{
				if(stop_playback)
				{
					return;
				}
				std::this_thread::sleep_until(start_time + sum_of_all_event_delays_till_now + event->time_since_last_event);
				event->playback();
				sum_of_all_event_delays_till_now += event->time_since_last_event;
			}
			c_callback_for_status_reporting(RecordPlaybackDLLEnums::PlaybackFinished); // <- ye this is pretty dumb
		}
	};
	playback_list_thread.detach();
}