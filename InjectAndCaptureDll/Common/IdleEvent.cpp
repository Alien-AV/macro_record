#include "IdleEvent.h"


IdleEvent::IdleEvent(std::chrono::duration<__int64,std::nano> _duration)
{
	duration = _duration;
}


IdleEvent::~IdleEvent()
{
}

void IdleEvent::print(std::ostream & where) const
{
	where << duration.count();
}
