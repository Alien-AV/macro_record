#include "Event.h"

Event::Event()
{
}


Event::~Event()
{
}

Event * Event::deserialize(std::string str)
{
	//TODO: implement
	return nullptr;
}

__declspec(dllexport) std::ostream &operator<<(std::ostream &outstream, Event const &event) {
	event.print(outstream);
	return outstream;
}