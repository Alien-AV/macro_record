// The following ifdef block is the standard way of creating macros which make exporting 
// from a DLL simpler. All files within this DLL are compiled with the INJECTANDCAPTUREDLL_EXPORTS
// symbol defined on the command line. This symbol should not be defined on any project
// that uses this DLL. This way any other project whose source files include this file see 
// INJECTANDCAPTUREDLL_API functions as being imported from a DLL, whereas this DLL sees symbols
// defined with this macro as being exported.
#ifdef INJECTANDCAPTUREDLL_EXPORTS
#define INJECTANDCAPTUREDLL_API __declspec(dllexport)
#else
#define INJECTANDCAPTUREDLL_API __declspec(dllimport)
#endif

#include "Common\Event.h"
#include "Common\KeyboardEvent.h"
#include "Common\MouseEvent.h"
#include "Common\IdleEvent.h"
#include <memory>

//INJECTANDCAPTUREDLL_API bool InjectEvent(KeyboardEvent &keyboardEvent);
//INJECTANDCAPTUREDLL_API bool InjectEvent(MouseEvent &mouseEvent);
//INJECTANDCAPTUREDLL_API bool InjectEvent(IdleEvent &idleEvent);

INJECTANDCAPTUREDLL_API void Init(void);

typedef void(*CaptureEventsCallback)(std::unique_ptr<Event>);

INJECTANDCAPTUREDLL_API BOOL StartCapture(CaptureEventsCallback);

INJECTANDCAPTUREDLL_API BOOL StopCapture(void);

INJECTANDCAPTUREDLL_API std::ostream &operator<<(std::ostream &outstream, Event const &event);