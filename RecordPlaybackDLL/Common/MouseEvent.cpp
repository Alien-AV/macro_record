#include "MouseEvent.h"
#include "../Playback/PlaybackInput.h"

MouseEvent::MouseEvent() = default;

MouseEvent::MouseEvent(LONG x, LONG y, DWORD action_type, DWORD wheelRotation, bool mappedToVirtualDesktop, bool relative_position)
						:x(x),y(y),ActionType(action_type),wheelRotation(wheelRotation),mappedToVirtualDesktop(mappedToVirtualDesktop),relative_position(relative_position)
{}

MouseEvent::~MouseEvent() = default;

void MouseEvent::print(std::ostream & where) const
{
	Event::print(where);
	where << "x: " << x << ", y: " << y << ", ActionType: " << ActionType << ", wheelRotation: " << wheelRotation << ", relative_position: " << relative_position << ", mappedToVirtualDesktop: " << mappedToVirtualDesktop << std::endl;
}

void MouseEvent::playback() const
{
	WindowsInjectionAPI::playback_mouse_event(x, y, wheelRotation, relative_position, ActionType);
}

std::unique_ptr<std::vector<unsigned char>> MouseEvent::serialize() const
{
	auto serialized_event = std::make_unique<protobufGenerated::ProtobufInputEvent>();
	auto serialized_mouse_event = serialized_event->mutable_mouseevent();
	serialized_mouse_event->set_x(x);
	serialized_mouse_event->set_y(y);
	serialized_mouse_event->set_actiontype(ActionType);
	serialized_mouse_event->set_wheelrotation(wheelRotation);
	serialized_mouse_event->set_relativeposition(relative_position);
	serialized_mouse_event->set_mappedtovirtualdesktop(mappedToVirtualDesktop);

	serialized_event->set_timesincestartofrecording(time_since_start_of_recording.count());

	return input_event_to_uchar_vector(serialized_event);
}
