#include "stdafx.h"
#include <thread>
#include <chrono>
#include <list>

#include "../InjectAndCaptureDll/InjectAndCaptureDll.h"
#include <conio.h>
#include <vector>
#include <iostream>

std::list<std::vector<unsigned char>> serialized_events;

const auto time_to_capture = std::chrono::seconds(5);
const auto time_to_sleep_after_capture = std::chrono::seconds(1);

const auto time_between_events = std::chrono::milliseconds(20);

void c_style_event_cb(const unsigned char buffer[], int buf_size) {
	const std::vector<unsigned char> serialized_event_vec(buffer, buffer+buf_size);
	serialized_events.push_back(serialized_event_vec);
}
void c_style_error_cb(const char* error)
{
	std::cout << "got error:" << error << std::endl;
}

int main()
{
	std::cout << "started" << std::endl;
	iac_dll_init(c_style_event_cb, c_style_error_cb);
	std::cout << "initialized. press a key to start capturing" << std::endl;
	_getch();

	iac_dll_start_capture();
	std::cout << "started capture" << std::endl;
	std::this_thread::sleep_for(time_to_capture);
	iac_dll_stop_capture();
	std::cout << "stopped capture, events in queue: " << serialized_events.size() << std::endl;
	std::this_thread::sleep_for(time_to_sleep_after_capture);

	for each(auto serialized_event in serialized_events) {
		iac_dll_inject_event(serialized_event.data(),serialized_event.size());
		std::this_thread::sleep_for(time_between_events);
	}
	std::cout << "finished playback" << std::endl;

	_getch();
	_getch();
	std::cout << "done" << std::endl;
}
