#pragma once
#include "Event.h"
#include <chrono>
#include "..\InjectAndCaptureDll.h"

class IdleEvent :
	public Event
{
public:
	INJECTANDCAPTUREDLL_API IdleEvent(std::chrono::duration<__int64, std::nano> _duration);
	INJECTANDCAPTUREDLL_API ~IdleEvent();
	void print(std::ostream& where) const;
	std::string serialize() const;
	void inject() const;
	std::chrono::duration<__int64, std::nano> duration;
};

