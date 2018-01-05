#pragma once
#include "..\stdafx.h"
#include "..\Common\Event.h"

class CaptureInput
{
public:
	DWORD lastError = 0;
	CaptureInput();
	~CaptureInput();
	bool startCapturing(int(*callback)(Event*));
	std::list<Event*> stopCapturing();
};

