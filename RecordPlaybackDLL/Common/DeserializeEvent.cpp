#include "..\stdafx.h"
#include "DeserializeEvent.h"
#include "Event.h"
#include "KeyboardEvent.h"
#include "MouseEvent.h"
#include "WaitEvent.h"
#include <limits>
#include <stdexcept>

std::unique_ptr<Event> make_event_from_protobuf_input_event(const protobufGenerated::ProtobufInputEvent& serialized_event)
{
	if (serialized_event.timesincelastevent() > static_cast<uint64_t>((std::numeric_limits<std::chrono::microseconds::rep>::max)()))
		throw std::invalid_argument("Event delay is too large");
	switch(serialized_event.Event_case())
	{
	case protobufGenerated::ProtobufInputEvent::EventCase::kKeyboardEvent:
		{
			const auto& serialized_kbdevent = serialized_event.keyboardevent();
			if (serialized_kbdevent.virtualkeycode() == 0 || serialized_kbdevent.virtualkeycode() > 255)
				throw std::invalid_argument("Invalid virtual key");
			auto kbdevent = std::make_unique<KeyboardEvent>();
			
			kbdevent->virtualKeyCode = serialized_kbdevent.virtualkeycode();
			kbdevent->keyUp = serialized_kbdevent.keyup();
			kbdevent->time_since_last_event = std::chrono::microseconds(serialized_event.timesincelastevent());
			
			return kbdevent;
		}
	case protobufGenerated::ProtobufInputEvent::EventCase::kMouseEvent:
		{
			const auto& serialized_mouseevent = serialized_event.mouseevent();
			auto mouseevent = std::make_unique<MouseEvent>(serialized_mouseevent.x(), serialized_mouseevent.y(), serialized_mouseevent.actiontype(),
			                                               serialized_mouseevent.wheelrotation(), serialized_mouseevent.mappedtovirtualdesktop(), serialized_mouseevent.relativeposition());

			mouseevent->time_since_last_event = std::chrono::microseconds(serialized_event.timesincelastevent());

			return mouseevent;
		}		
	case protobufGenerated::ProtobufInputEvent::EventCase::kWaitCondition:
		{
			if (!WaitEvent::valid(serialized_event.waitcondition())) throw std::invalid_argument("Invalid wait condition");
			auto wait = std::make_unique<WaitEvent>();
			wait->condition = serialized_event.waitcondition();
			wait->time_since_last_event = std::chrono::microseconds(serialized_event.timesincelastevent());
			return wait;
		}
	default:
		throw std::invalid_argument("Missing input event payload");
	}
}

std::unique_ptr<Event> record_playback::deserialize_event(std::vector<unsigned char> serialized_event_vec)
{
	auto serialized_event = std::make_unique<protobufGenerated::ProtobufInputEvent>();
	if (serialized_event_vec.size() > static_cast<size_t>((std::numeric_limits<int>::max)()) ||
		!serialized_event->ParseFromArray(serialized_event_vec.data(), static_cast<int>(serialized_event_vec.size())))
		throw std::invalid_argument("Malformed input event");

	return make_event_from_protobuf_input_event(*serialized_event);
}

std::vector<std::unique_ptr<Event>> record_playback::deserialize_events(std::vector<unsigned char> serialized_events_vec)
{
	auto serialized_events = std::make_unique<protobufGenerated::ProtobufInputEventList>();
	if (serialized_events_vec.size() > static_cast<size_t>((std::numeric_limits<int>::max)()) ||
		!serialized_events->ParseFromArray(serialized_events_vec.data(), static_cast<int>(serialized_events_vec.size())) ||
		serialized_events->inputevents_size() == 0)
		throw std::invalid_argument("Malformed or empty input event list");
	std::vector<std::unique_ptr<Event>> deserialized_events_vec;
	deserialized_events_vec.reserve(serialized_events->inputevents_size());
	
	for (int i = 0; i < serialized_events->inputevents_size(); ++i)
	{
		deserialized_events_vec.push_back( make_event_from_protobuf_input_event(serialized_events->inputevents(i)) );
	}

	return deserialized_events_vec;
}
