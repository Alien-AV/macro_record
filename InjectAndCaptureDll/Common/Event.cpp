#include "Event.h"
#include <memory>
Event::Event()
{
}


Event::~Event()
{
}

__declspec(dllexport) std::ostream &operator<<(std::ostream &outstream, Event const &event) {
	event.print(outstream);
	return outstream;
}