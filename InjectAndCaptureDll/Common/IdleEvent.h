#pragma once
#include "Event.h"
#include <chrono>
class IdleEvent :
	public Event
{
public:
	IdleEvent(std::chrono::duration<__int64, std::nano> _duration);
	~IdleEvent();
	void print(std::ostream& where) const;
private:
	std::chrono::duration<__int64, std::nano> duration;
};

