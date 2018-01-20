#pragma once
#include <chrono>

class Event
{
public:
	Event();
	virtual ~Event();

	static Event* deserialize(std::string);
	virtual void print(std::ostream& where) const = 0;
	virtual std::string serialize() const = 0;
	virtual void inject() const = 0;
	std::chrono::duration<__int64, std::nano> idleDurationBefore;
private:
};

