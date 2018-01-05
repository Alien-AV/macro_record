#pragma once
#include "../stdafx.h"

class Event
{
public:
	Event();
	virtual ~Event();

	virtual void print(std::ostream& where) const = 0;
private:

};

std::ostream &operator<<(std::ostream &outstream, Event const &event);