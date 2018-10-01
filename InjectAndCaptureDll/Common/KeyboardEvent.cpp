#include "KeyboardEvent.h"
#include "..\Inject\InjectInput.h"
#include "protobuf/cpp/Events.pb.h"

KeyboardEvent::KeyboardEvent() : virtualKeyCode(0), hardwareScanCode(0), keyUp(false)
{
}

KeyboardEvent::~KeyboardEvent() = default;

void KeyboardEvent::print(std::ostream & where) const
{
	Event::print(where);
	where << "virtualKeyCode: " << virtualKeyCode << ", keyUp: " << keyUp << std::endl;
}

void KeyboardEvent::inject() const
{
	WindowsInjectionAPI::inject_keyboard_event(virtualKeyCode, keyUp);
}

std::unique_ptr<std::vector<unsigned char>> KeyboardEvent::serialize() const
{
	auto serialized_event = std::make_unique<InputEvent>();
	auto serialized_keyboard_event = serialized_event->mutable_keyboardevent();
	serialized_keyboard_event->set_virtualkeycode(virtualKeyCode);
	serialized_keyboard_event->set_keyup(keyUp);

	serialized_event->set_timesincestartofrecording(time_since_start_of_recording.count());
	
	return input_event_to_uchar_vector(serialized_event);
}
