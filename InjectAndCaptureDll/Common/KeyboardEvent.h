#pragma once

#include "..\InjectAndCaptureDll.h"
#include "Event.h"
class KeyboardEvent :
	public Event
{
public:
	WORD virtualKeyCode;
	WORD hardwareScanCode;
	bool keyUp;


	INJECTANDCAPTUREDLL_API KeyboardEvent();
	INJECTANDCAPTUREDLL_API ~KeyboardEvent();

	std::string serialize() const;
	void print(std::ostream& where) const;
	void inject() const;
};

