#include "MouseEvent.h"
#include "..\Inject\InjectInput.h"

MouseEvent::MouseEvent()
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
	WindowsInjectionAPI::inject_mouse_event(x, y, wheelRotation, relative_position, ActionType);
}

std::string MouseEvent::serialize() const
{
	std::stringstream dest_buff;
	//auto timeInSecondsFloat = std::chrono::duration_cast<std::chrono::duration<float,std::micro>>(timeSinceStartOfRecording);
	dest_buff << "{m," << x << "," << y << "," << wheelRotation << "," << relative_position << "," << mappedToVirtualDesktop << "," << ActionType << "," << timeSinceStartOfRecording.count() << "}";
	return dest_buff.str();
}
