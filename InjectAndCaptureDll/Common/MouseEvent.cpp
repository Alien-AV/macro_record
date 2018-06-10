#include "MouseEvent.h"
#include "..\Inject\InjectInput.h"

MouseEvent::MouseEvent() : x(0), y(0), wheelRotation(0), mappedToVirtualDesktop(false), ActionType(0)
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
	WindowsInjectionAPI::inject_mouse_event(x, y, wheelRotation, ActionType);
}

std::string MouseEvent::serialize() const
{
	std::stringstream dest_buff;
	//auto timeInSecondsFloat = std::chrono::duration_cast<std::chrono::duration<float,std::micro>>(timeSinceStartOfRecording);
	dest_buff << "{m," << x << "," << y << "," << wheelRotation << "," << mappedToVirtualDesktop << "," << ActionType << "," << timeSinceStartOfRecording.count() << "}";
	return dest_buff.str();
}
