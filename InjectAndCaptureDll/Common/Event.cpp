#include "Event.h"
#include "IdleEvent.h"
#include "MouseEvent.h"
#include "KeyboardEvent.h"

Event::Event()
{
}


Event::~Event()
{
}

Event * Event::deserialize(std::string)
{
	//TODO: implement
	return nullptr;
}

__declspec(dllexport) std::ostream &operator<<(std::ostream &outstream, Event const &event) {
	event.print(outstream);
	return outstream;
}