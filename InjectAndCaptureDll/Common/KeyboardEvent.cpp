#include <sstream>
#include "KeyboardEvent.h"
#include "..\Inject\InjectInput.h"

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
	std::stringstream dest_buff;
	//auto timeInSecondsFloat = std::chrono::duration_cast<std::chrono::duration<float, std::micro>>(timeSinceStartOfRecording);
	dest_buff << "{k," << virtualKeyCode << "," << keyUp << "," << timeSinceStartOfRecording.count() << "}";
	return dest_buff.str();
}
