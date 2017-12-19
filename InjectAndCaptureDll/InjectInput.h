#pragma once
#include "KeyboardEvent.h"
#include "MouseEvent.h"

class InjectInput
{
public:
	DWORD lastError = 0;

	InjectInput();
	~InjectInput();
	bool InjectEvent(KeyboardEvent keyboardEvent);
	bool InjectEvent(MouseEvent mouseEvent);
};

