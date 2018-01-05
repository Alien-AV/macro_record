// InjectAndCaptureDll.cpp : Defines the exported functions for the DLL application.
//

#include <memory>
#include "stdafx.h"
#include "InjectAndCaptureDll.h"
#include "Inject\InjectInput.h"


// This is an example of an exported function.
INJECTANDCAPTUREDLL_API int InjectEvent(void)
{
	auto inputInjector = std::make_unique<InjectInput>();
	Sleep(3000);
	MouseEvent me;
	me.ActionType = MouseEvent::ActionTypeFlag::Move | MouseEvent::ActionTypeFlag::LeftDown;
	me.x = 200; me.y = 200;
	inputInjector->InjectEvent(me);

	Sleep(100);
	me.ActionType = MouseEvent::ActionTypeFlag::LeftUp;
	inputInjector->InjectEvent(me);

	Sleep(3000);
	return 0;
}

INJECTANDCAPTUREDLL_API int RecordEvents(void)
{
	return 42;
}
