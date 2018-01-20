#pragma once
#include "../Common/KeyboardEvent.h"
#include "../Common/MouseEvent.h"
#include "../Common/IdleEvent.h"

class InjectInput
{
public:
	DWORD lastError = 0;

	InjectInput();
	~InjectInput();
};

bool InjectEvent(KeyboardEvent keyboardEvent);
bool InjectEvent(MouseEvent mouseEvent);
bool InjectEvent(IdleEvent idleEvent);