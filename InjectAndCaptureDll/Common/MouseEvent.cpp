#include "MouseEvent.h"
#include "../Inject/InjectInput.h"
#include "protobuf/cpp/Events.pb.h"

MouseEvent::MouseEvent()
{
}

MouseEvent::MouseEvent(LONG x, LONG y, DWORD action_type, DWORD wheelRotation, bool mappedToVirtualDesktop, bool relative_position)
						:x(x),y(y),ActionType(action_type),wheelRotation(wheelRotation),mappedToVirtualDesktop(mappedToVirtualDesktop),relative_position(relative_position)
{}

MouseEvent::~MouseEvent()
{
}

void MouseEvent::print(std::ostream & where) const
{
	where << serialize();
}

void MouseEvent::inject() const
{
	WindowsInjectionAPI::inject_mouse_event(x, y, wheelRotation, relative_position, ActionType);
}

std::string MouseEvent::serialize() const
{
	auto serialized_event = std::make_unique<InputEvent>();
	auto serialized_mouse_event = serialized_event->mutable_mouseevent();
	serialized_mouse_event->set_x(x);
	serialized_mouse_event->set_y(y);
	serialized_mouse_event->set_actiontype(ActionType);
	serialized_mouse_event->set_wheelrotation(wheelRotation);
	serialized_mouse_event->set_relativeposition(relative_position);
	serialized_mouse_event->set_mappedtovirtualdesktop(mappedToVirtualDesktop);

	serialized_event->set_timesincestartofrecording(time_since_start_of_recording.count());

	std::string result_string;
	if(!serialized_event->SerializeToString(&result_string))
	{
		return "";
	}
	return result_string;
}
