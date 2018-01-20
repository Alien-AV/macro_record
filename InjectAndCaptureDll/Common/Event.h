#pragma once
#include "../stdafx.h"

class Event
{
public:
	Event();
	virtual ~Event();

	static Event* deserialize(std::string);
	virtual void print(std::ostream& where) const = 0;
	virtual std::string serialize() const = 0;
	virtual void inject() const = 0;
private:

};

