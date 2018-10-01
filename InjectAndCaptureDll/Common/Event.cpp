#include "Event.h"
#include <vector>

Event::Event() = default;
Event::~Event() = default;

void Event::print(std::ostream& where) const
{
	where << "time_since_start_of_recording(nano):" << time_since_start_of_recording.count();
}

std::unique_ptr<std::vector<unsigned char>> Event::input_event_to_uchar_vector(const std::unique_ptr<InputEvent>& serialized_event)
{
	auto serialized_buf_size = serialized_event->ByteSizeLong();
	auto serialized_buf = std::make_unique<std::vector<unsigned char>>(serialized_buf_size); //TODO: make sure you can initialize vectors this way. do you even need to make unique_ptr for vectors?
	if(!serialized_event->SerializeToArray(serialized_buf->data(), serialized_buf_size))
	{
		return nullptr;
	}

	return serialized_buf;
}

__declspec(dllexport) std::ostream &operator<<(std::ostream &outstream, Event const &event) {
	event.print(outstream);
	return outstream;
}
