#include "..\stdafx.h"
#include "DeserializeEvent.h"
#include "Event.h"
#include "KeyboardEvent.h"
#include "MouseEvent.h"

std::unique_ptr<Event> make_event_from_protobuf_input_event(const protobufGenerated::ProtobufInputEvent& serialized_event)
{
	switch(serialized_event.Event_case())
	{
	case protobufGenerated::ProtobufInputEvent::EventCase::kKeyboardEvent:
		{
			const auto& serialized_kbdevent = serialized_event.keyboardevent();
			auto kbdevent = std::make_unique<KeyboardEvent>();
			
			kbdevent->virtualKeyCode = serialized_kbdevent.virtualkeycode();
			kbdevent->keyUp = serialized_kbdevent.keyup();
			kbdevent->time_since_last_event = std::chrono::microseconds(serialized_event.timesincelastevent());
			
			return std::move(kbdevent);
		}
	case protobufGenerated::ProtobufInputEvent::EventCase::kMouseEvent:
		{
			const auto& serialized_mouseevent = serialized_event.mouseevent();
			auto mouseevent = std::make_unique<MouseEvent>(serialized_mouseevent.x(), serialized_mouseevent.y(), serialized_mouseevent.actiontype(),
			                                               serialized_mouseevent.wheelrotation(), serialized_mouseevent.mappedtovirtualdesktop(), serialized_mouseevent.relativeposition());

			mouseevent->time_since_last_event = std::chrono::microseconds(serialized_event.timesincelastevent());

			return std::move(mouseevent);
		}		
	default:
		//TODO: handle error here?
		break;
	}
	return nullptr;
}

std::unique_ptr<Event> record_playback::deserialize_event(std::vector<unsigned char> serialized_event_vec) //TODO: validations maybe? error codes?
{
	auto serialized_event = std::make_unique<protobufGenerated::ProtobufInputEvent>();
	serialized_event->ParseFromArray(serialized_event_vec.data(), int(serialized_event_vec.size()));

	return make_event_from_protobuf_input_event(*serialized_event);
}

std::vector<std::unique_ptr<Event>> record_playback::deserialize_events(std::vector<unsigned char> serialized_events_vec)
{
	auto serialized_events = std::make_unique<protobufGenerated::ProtobufInputEventList>();
	serialized_events->ParseFromArray(serialized_events_vec.data(), static_cast<int>(serialized_events_vec.size()));
	std::vector<std::unique_ptr<Event>> deserialized_events_vec;
	deserialized_events_vec.reserve(serialized_events->inputevents_size());
	
	for (int i = 0; i < serialized_events->inputevents_size(); ++i)
	{
		deserialized_events_vec.push_back( make_event_from_protobuf_input_event(serialized_events->inputevents(i)) );
	}

	return deserialized_events_vec;
}