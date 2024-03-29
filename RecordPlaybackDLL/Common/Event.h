#pragma once
#include <chrono>
#include <vector>
#include "../../Common/protobuf/cpp/Events.pb.h"

class Event
{
public:
	Event();
	virtual ~Event();

	virtual void print(std::ostream& where) const;
	virtual std::unique_ptr<std::vector<unsigned char>> serialize() const = 0;
	virtual void playback() const = 0;
	std::chrono::microseconds time_since_last_event{};
protected:
	static std::unique_ptr<std::vector<unsigned char>> input_event_to_uchar_vector(const std::unique_ptr<protobufGenerated::ProtobufInputEvent>&);
};

