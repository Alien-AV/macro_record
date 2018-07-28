#include "..\stdafx.h"
#include <cassert>
#include "Event.h"
#include "KeyboardEvent.h"
#include "MouseEvent.h"


std::unique_ptr<Event> iac_dll::deserializeEvent(std::string str) //TODO: validations maybe? error codes?
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
		kbdevent->timeSinceStartOfRecording = std::chrono::nanoseconds(durationNanoseconds);
		return std::move(kbdevent);
		break;
	case 'm':
		LONG x, y;
		DWORD wheelRotation;
		bool relative_position;
		bool mappedToVirtualDesktop;
		DWORD ActionType;

		strstr.ignore(1, ',');
		strstr >> x;
		strstr.ignore(1, ',');
		strstr >> y;
		strstr.ignore(1, ',');
		strstr >> wheelRotation;
		strstr.ignore(1, ',');
		strstr >> relative_position;
		strstr.ignore(1, ',');
		strstr >> mappedToVirtualDesktop;
		strstr.ignore(1, ',');
		strstr >> ActionType;
		strstr.ignore(1, ',');
		strstr >> durationNanoseconds;

		mouseevent = std::make_unique<MouseEvent>(); //TODO: fill the mouseevent fields directly from strstr >>
		mouseevent->x = x;
		mouseevent->y = y;
		mouseevent->wheelRotation = wheelRotation;
		mouseevent->relative_position = relative_position;
		mouseevent->mappedToVirtualDesktop = mappedToVirtualDesktop;
		mouseevent->ActionType = ActionType;
		mouseevent->timeSinceStartOfRecording = std::chrono::nanoseconds(durationNanoseconds);
		return std::move(mouseevent);
		break;
	default:
		assert("unknown event type"); //TODO: is this thing legal?
	}
	return nullptr;
}
