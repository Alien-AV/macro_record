#pragma once
#include <chrono>
#include "protobuf/cpp/Events.pb.h"
#include <vector>

class Event
{
public:
	Event();
	virtual ~Event();

	virtual void print(std::ostream& where) const;
	virtual std::unique_ptr<std::vector<unsigned char>> serialize() const = 0;
	virtual void inject() const = 0;
	std::chrono::microseconds time_since_start_of_recording{};
protected:
	static std::unique_ptr<std::vector<unsigned char>> input_event_to_uchar_vector(const std::unique_ptr<InputEvent>&);
};

