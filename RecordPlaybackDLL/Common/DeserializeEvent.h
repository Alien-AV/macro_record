#pragma once
#include "../RecordPlaybackDLL.h"
#include "Event.h"
#include <memory>
#include <vector>

namespace record_playback {
	RECORD_PLAYBACK_DLL_API std::ostream &operator<<(std::ostream &outstream, Event const &event); //TODO: doesn't belong here
	RECORD_PLAYBACK_DLL_API std::unique_ptr<Event> deserialize_event(std::vector<unsigned char>);
	RECORD_PLAYBACK_DLL_API std::vector<std::unique_ptr<Event>> deserialize_events(std::vector<unsigned char>);
}
