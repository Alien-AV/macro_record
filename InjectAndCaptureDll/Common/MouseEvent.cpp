#include "MouseEvent.h"
#include "..\Inject\InjectInput.h"

MouseEvent::MouseEvent() : x(0), y(0), wheelRotation(0), useRelativePosition(false), mappedToVirtualDesktop(false), ActionType(0)
{
}


MouseEvent::~MouseEvent()
{
}

void MouseEvent::print(std::ostream & where) const
{
	where << serialize();
}

void MouseEvent::inject() const
{
	InjectEvent(*this);
}

std::string MouseEvent::serialize() const
{
	std::stringstream dest_buff;
	dest_buff << "{m," << x << "," << y << "," << wheelRotation << "," << useRelativePosition << "," << mappedToVirtualDesktop << "," << ActionType << "," << idleDurationBefore.count() << "}";
	return dest_buff.str();
}
