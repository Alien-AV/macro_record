#include "..\stdafx.h"
#include <cassert>
#include "Event.h"
#include "KeyboardEvent.h"
#include "MouseEvent.h"
#include "protobuf/cpp/Events.pb.h"


std::unique_ptr<Event> iac_dll::deserialize_event(std::vector<unsigned char> serialized_event_vec) //TODO: validations maybe? error codes?
{
	auto serialized_event = std::make_unique<InputEvent>();
	serialized_event->ParseFromArray(serialized_event_vec.data(), serialized_event_vec.size());

	switch(serialized_event->Event_case())
	{
		case InputEvent::EventCase::kKeyboardEvent:
		{
			auto serialized_kbdevent = serialized_event->keyboardevent();
			auto kbdevent = std::make_unique<KeyboardEvent>();
			
			kbdevent->virtualKeyCode = serialized_kbdevent.virtualkeycode();
			kbdevent->keyUp = serialized_kbdevent.keyup();
			kbdevent->time_since_start_of_recording = std::chrono::nanoseconds(serialized_event->timesincestartofrecording());
			
			return std::move(kbdevent);
		}
		case InputEvent::EventCase::kMouseEvent:
		{
			auto serialized_mouseevent = serialized_event->mouseevent();
			auto mouseevent = std::make_unique<MouseEvent>(serialized_mouseevent.x(), serialized_mouseevent.y(), serialized_mouseevent.actiontype(),
				serialized_mouseevent.wheelrotation(), serialized_mouseevent.mappedtovirtualdesktop(), serialized_mouseevent.relativeposition());

			mouseevent->time_since_start_of_recording = std::chrono::nanoseconds(serialized_event->timesincestartofrecording());

			return std::move(mouseevent);
		}		
		default:
			//TODO: handle error here?
			return nullptr;
		break;
	}
}
