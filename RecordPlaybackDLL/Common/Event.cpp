#include "Event.h"
#include <vector>
#include <limits>
#include "../RecordPlaybackDLL.h"

Event::Event() = default;
Event::~Event() = default;

void Event::print(std::ostream& where) const
{
	where << "time_since_last_event(nano):" << time_since_last_event.count();
}

std::unique_ptr<std::vector<unsigned char>> Event::input_event_to_uchar_vector(const std::unique_ptr<protobufGenerated::ProtobufInputEvent>& serialized_event)
{
	const auto serialized_buf_size = serialized_event->ByteSizeLong();
	if (serialized_buf_size > static_cast<size_t>((std::numeric_limits<int>::max)())) return nullptr;
	auto serialized_buf = std::make_unique<std::vector<unsigned char>>(serialized_buf_size);
	if(!serialized_event->SerializeToArray(serialized_buf->data(), static_cast<int>(serialized_buf_size)))
	{
		return nullptr;
	}

	return serialized_buf;
}

namespace record_playback{
	RECORD_PLAYBACK_DLL_API std::ostream &operator<<(std::ostream &outstream, Event const &event) {
		event.print(outstream);
		return outstream;
	}
}
