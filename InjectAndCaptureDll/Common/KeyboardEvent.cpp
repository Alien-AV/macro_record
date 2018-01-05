#include "KeyboardEvent.h"


KeyboardEvent::KeyboardEvent() : virtualKeyCode(0), hardwareScanCode(0), keyUp(false)
{
}


KeyboardEvent::~KeyboardEvent()
{
}

void KeyboardEvent::print(std::ostream & where) const
{
	where << hardwareScanCode << keyUp;
}
