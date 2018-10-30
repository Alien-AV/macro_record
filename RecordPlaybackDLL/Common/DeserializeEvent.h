#pragma once
#include "../InjectAndCaptureDll.h"
#include "Event.h"
#include <memory>
#include <vector>

namespace iac_dll {
	// //TODO: need to add "InjectEvent" export?
	INJECTANDCAPTUREDLL_API std::ostream &operator<<(std::ostream &outstream, Event const &event); //TODO: doesn't belong here
	INJECTANDCAPTUREDLL_API std::unique_ptr<Event> deserialize_event(std::vector<unsigned char>);
	INJECTANDCAPTUREDLL_API std::vector<std::unique_ptr<Event>> deserialize_events(std::vector<unsigned char>);
}
