#pragma once
#include <chrono>
#include <sstream>

class Event
{
public:
	Event();
	virtual ~Event();

	virtual void print(std::ostream& where) const = 0;
	virtual std::string serialize() const = 0;
	virtual void inject() const = 0;
	std::chrono::duration<__int64, std::nano> time_since_start_of_recording;
private:
};

