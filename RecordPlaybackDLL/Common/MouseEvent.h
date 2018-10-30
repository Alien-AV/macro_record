#pragma once
#include "Event.h"
#include "..\InjectAndCaptureDll.h"

class MouseEvent :
	public Event
{
public:
	class ActionTypeFlags
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
	// more flags here:
	// https://msdn.microsoft.com/en-us/library/windows/desktop/ms646273(v=vs.85).aspx


	LONG x = 0, y = 0; //TODO: change to signed int?
	DWORD ActionType = 0;
	DWORD wheelRotation = 0;
	bool mappedToVirtualDesktop = false;
	bool relative_position = false;
	
	INJECTANDCAPTUREDLL_API MouseEvent();
	INJECTANDCAPTUREDLL_API MouseEvent(LONG x, LONG y, DWORD action_type, DWORD wheelRotation, bool mappedToVirtualDesktop, bool relative_position);
	INJECTANDCAPTUREDLL_API ~MouseEvent();
	std::unique_ptr<std::vector<unsigned char>> serialize() const override;
	void print(std::ostream& where) const override;
	void inject() const override;
};