#pragma once

#include "Event.h"
class KeyboardEvent :
	public Event
{
public:
	WORD virtualKeyCode;
	WORD hardwareScanCode;
	bool keyUp;


	KeyboardEvent();
	~KeyboardEvent();
};

