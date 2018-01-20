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

void ProcessEvent(std::unique_ptr<Event> event) {
	//std::cout << (*event) << std::endl;
	event_list.push_back(std::move(event));
}



const unsigned int SleepTime = 1;

int main()
{
	puts("started");
	Init();
	puts("initialized.\npress a key to start capturing");
	_getch();
	std::cout << "sleeping " << SleepTime << std::endl;
	std::this_thread::sleep_for(std::chrono::seconds(SleepTime));
	auto before_capture = std::chrono::high_resolution_clock::now();
	auto result = StartCapture(ProcessEvent);
	printf("started capturing, retval: %d.\npress a key to stop capturing\n", result);
	_getch();
	result = StopCapture();
	auto after_capture = std::chrono::high_resolution_clock::now();
	std::cout << "stopped capturing, time: " << (after_capture-before_capture).count() << ", sleeping " << SleepTime << " to playback" << std::endl;
	std::this_thread::sleep_for(std::chrono::seconds(SleepTime));
	printf("playing back\n");

	auto before_playback = std::chrono::high_resolution_clock::now();
	auto current_playback_time_offset = std::chrono::high_resolution_clock::now();
	for each (auto &event in event_list)
	{
		//std::cout << (*event);

		auto maybe_idle_event = dynamic_cast<IdleEvent*>(event.get());
		if (maybe_idle_event != nullptr) {
			current_playback_time_offset += maybe_idle_event->duration;
			maybe_idle_event->duration = (current_playback_time_offset - std::chrono::high_resolution_clock::now());
		}
		event->inject();
	}
	auto after_playback = std::chrono::high_resolution_clock::now();
	std::cout << "stopped playing, time: " << (after_playback - before_playback).count() << std::endl;
	_getch();
	_getch();
}
