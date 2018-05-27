#include "stdafx.h"

#include <thread>
#include <memory>
#include <chrono>
#include <fstream>
#include <iostream>
#include <list>

#include "../InjectAndCaptureDll/InjectAndCaptureDll.h"
#include <conio.h>


std::list<std::unique_ptr<Event>> event_list;

std::list<const char*> serialized_events;

void ProcessEvent(std::unique_ptr<Event> event) {
	//std::cout << (*event) << std::endl;
	event_list.push_back(std::move(event));
}



const unsigned int SleepTime = 1;


void cb(const char evt[]) {
	std::cout << evt << std::endl;
	serialized_events.push_back(_strdup(evt));
}

int main()
{
	puts("started");
	iac_dll::Init();
	//iac_dll_init();
	puts("initialized.\npress a key to start capturing");
	_getch();

	iac_dll_start_capture(cb);
	std::this_thread::sleep_for(std::chrono::seconds(1));
	iac_dll_stop_capture();

	std::this_thread::sleep_for(std::chrono::seconds(1));

	for each(auto serialized_event in serialized_events) {
		iac_dll_inject_event(serialized_event);
		std::this_thread::sleep_for(std::chrono::milliseconds(5));
	}

	//std::cout << "sleeping " << SleepTime << std::endl;
	//std::this_thread::sleep_for(std::chrono::seconds(SleepTime));
	//auto before_capture = std::chrono::high_resolution_clock::now();
	//auto result = iac_dll::StartCapture(ProcessEvent);
	//printf("started capturing, retval: %d.\npress a key to stop capturing\n", result);
	//_getch();
	//result = iac_dll::StopCapture();
	//auto after_capture = std::chrono::high_resolution_clock::now();
	//std::cout << "stopped capturing, time: " << (after_capture-before_capture).count() << ", sleeping " << SleepTime << " to playback" << std::endl;
	//std::this_thread::sleep_for(std::chrono::seconds(SleepTime));
	//printf("playing back\n");

	//auto before_playback = std::chrono::high_resolution_clock::now();
	//auto current_playback_time_offset = std::chrono::high_resolution_clock::now();
	//for each (auto &event in event_list)
	//{
 //		current_playback_time_offset += event->idleDurationBefore;
 //		auto actualIdleDuration = (current_playback_time_offset - std::chrono::high_resolution_clock::now());
	//	std::this_thread::sleep_for(actualIdleDuration);
	//	event->inject();
	//}

	//auto after_playback = std::chrono::high_resolution_clock::now();
	//std::cout << "stopped playing, time: " << (after_playback - before_playback).count() << std::endl;
	_getch();
	_getch();
}
