#pragma once
#include "Event.h"
class MouseEvent :
	public Event
{
public:
	LONG x, y;
	DWORD wheelRotation;
	bool useRelativePosition;
	class ActionTypeFlag
	{
	public:
		static const DWORD Move = MOUSEEVENTF_MOVE;
		static const DWORD LeftDown = MOUSEEVENTF_LEFTDOWN;
		static const DWORD LeftUp = MOUSEEVENTF_LEFTUP;
		static const DWORD RightDown = MOUSEEVENTF_RIGHTDOWN;
		static const DWORD RightUp = MOUSEEVENTF_RIGHTUP;
		static const DWORD MiddleDown = MOUSEEVENTF_MIDDLEDOWN;
		static const DWORD MiddleUp = MOUSEEVENTF_MIDDLEUP;
		static const DWORD XDown = MOUSEEVENTF_XDOWN;
		static const DWORD XUp = MOUSEEVENTF_XUP;
	};
	DWORD ActionType;

	// more flags here:
	// https://msdn.microsoft.com/en-us/library/windows/desktop/ms646273(v=vs.85).aspx
	

	MouseEvent();
	~MouseEvent();
};

