#include <sstream>
#include "IdleEvent.h"
#include "..\Inject\InjectInput.h"

IdleEvent::IdleEvent(std::chrono::duration<__int64,std::nano> _duration)
{
	duration = _duration;
}


IdleEvent::~IdleEvent()
{
}

void IdleEvent::print(std::ostream & where) const
{
	where << serialize();
}

std::string IdleEvent::serialize() const
{
	std::stringstream dest_buff;
	dest_buff << "{i," << duration.count() << "}";
	return dest_buff.str();
}

void IdleEvent::inject() const
{
	InjectEvent(*this);
}
