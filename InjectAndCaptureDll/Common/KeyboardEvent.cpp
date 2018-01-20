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
	InjectEvent(*this);
}

std::string KeyboardEvent::serialize() const
{
	std::stringstream dest_buff;
	dest_buff << "{k," << virtualKeyCode << "," << keyUp << "}";
	return dest_buff.str();
}
