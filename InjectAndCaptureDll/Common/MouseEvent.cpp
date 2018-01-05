#include <sstream>
#include "MouseEvent.h"


MouseEvent::MouseEvent() : x(0), y(0), wheelRotation(0), useRelativePosition(false), ActionType(0)
{
}


MouseEvent::~MouseEvent()
{
}

void MouseEvent::print(std::ostream & where) const
{
	where << x << y << wheelRotation << useRelativePosition << ActionType;
}

//std::string MouseEvent::Serialize()
//{
//	std::stringstream dest_buff;
//	dest_buff << x << y << wheelRotation << useRelativePosition << ActionType;
//	return dest_buff.str();
//}
