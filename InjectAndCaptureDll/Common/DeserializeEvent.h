#pragma once

#include "Event.h"
#include "KeyboardEvent.h"
#include "MouseEvent.h"

INJECTANDCAPTUREDLL_API std::unique_ptr<Event> deserializeEvent(std::string);