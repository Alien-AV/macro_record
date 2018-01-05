#include "Event.h"


Event::Event()
{
}


Event::~Event()
{
}

std::ostream &operator<<(std::ostream &outstream, Event const &event) {
	event.print(outstream);
	return outstream;
}