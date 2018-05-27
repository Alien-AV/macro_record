#pragma once
#include <chrono>
#include <memory>
#include <sstream>

class Event
{
public:
	Event();
	virtual ~Event();

	virtual void print(std::ostream& where) const = 0;
	virtual std::string serialize() const = 0;
	virtual void inject() const = 0;
	std::chrono::duration<__int64, std::nano> idleDurationBefore;
private:
};

