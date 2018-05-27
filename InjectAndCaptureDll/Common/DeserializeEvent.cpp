#include "..\stdafx.h"
#include "DeserializeEvent.h"

std::unique_ptr<Event> deserializeEvent(std::string str) //TODO: validations maybe? error codes?
{
	std::stringstream strstr(str);
	char type;
	std::unique_ptr<KeyboardEvent> kbdevent;
	std::unique_ptr<MouseEvent> mouseevent;
	long long durationNanoseconds;
	strstr.ignore(1, '{');
	strstr >> type;
	switch (type) {
	case 'k':
		WORD virtualKeyCode;
		bool keyUp;
		
		strstr.ignore(1, ',');
		strstr >> virtualKeyCode;
		strstr.ignore(1, ',');
		strstr >> keyUp;
		strstr.ignore(1, ',');
		strstr >> durationNanoseconds;

		kbdevent = std::make_unique<KeyboardEvent>();
		kbdevent->virtualKeyCode = virtualKeyCode;
		kbdevent->keyUp = keyUp;
		kbdevent->idleDurationBefore = std::chrono::nanoseconds(durationNanoseconds);
		return std::move(kbdevent);
		break;
	case 'm':
		LONG x, y;
		DWORD wheelRotation;
		bool useRelativePosition;
		bool mappedToVirtualDesktop;
		DWORD ActionType = 0;

		strstr.ignore(1, ',');
		strstr >> x;
		strstr.ignore(1, ',');
		strstr >> y;
		strstr.ignore(1, ',');
		strstr >> wheelRotation;
		strstr.ignore(1, ',');
		strstr >> useRelativePosition;
		strstr.ignore(1, ',');
		strstr >> mappedToVirtualDesktop;
		strstr.ignore(1, ',');
		strstr >> ActionType;
		strstr.ignore(1, ',');
		strstr >> durationNanoseconds;

		mouseevent = std::make_unique<MouseEvent>();
		mouseevent->x = x;
		mouseevent->y = y;
		mouseevent->wheelRotation = wheelRotation;
		mouseevent->useRelativePosition = useRelativePosition;
		mouseevent->mappedToVirtualDesktop = mappedToVirtualDesktop;
		mouseevent->ActionType = ActionType;
		mouseevent->idleDurationBefore = std::chrono::nanoseconds(durationNanoseconds);
		return std::move(mouseevent);
		break;
	}
	return nullptr;
}