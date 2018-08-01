#include <sstream>
#include "KeyboardEvent.h"
#include "..\Inject\InjectInput.h"
#include "protobuf/cpp/Events.pb.h"

KeyboardEvent::KeyboardEvent() : virtualKeyCode(0), hardwareScanCode(0), keyUp(false)
{
}


KeyboardEvent::~KeyboardEvent()
{
}

void KeyboardEvent::print(std::ostream & where) const
{
	where << serialize();
}

void KeyboardEvent::inject() const
{
	WindowsInjectionAPI::inject_keyboard_event(virtualKeyCode, keyUp);
}

std::string KeyboardEvent::serialize() const
{
	auto serialized_event = std::make_unique<InputEvent>();
	auto serialized_keyboard_event = serialized_event->mutable_keyboardevent();
	serialized_keyboard_event->set_virtualkeycode(virtualKeyCode);
	serialized_keyboard_event->set_keyup(keyUp);

	serialized_event->set_timesincestartofrecording(time_since_start_of_recording.count());

	std::string result_string;
	if(!serialized_event->SerializeToString(&result_string))
	{
		return "";
	}
	return result_string;
}
