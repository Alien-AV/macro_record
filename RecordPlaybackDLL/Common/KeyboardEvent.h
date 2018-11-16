#pragma once

#include "..\RecordPlaybackDLL.h"
#include "Event.h"
class KeyboardEvent :
	public Event
{
public:
	WORD virtualKeyCode;
	WORD hardwareScanCode;
	bool keyUp;

	RECORD_PLAYBACK_DLL_API KeyboardEvent();
	RECORD_PLAYBACK_DLL_API ~KeyboardEvent();

	std::unique_ptr<std::vector<unsigned char>> serialize() const override;
	void print(std::ostream& where) const override;
	void playback() const override;
};

