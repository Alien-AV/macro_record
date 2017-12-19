// macro_record.cpp : Defines the entry point for the console application.
//

#include "stdafx.h"
#include <fstream>
#include "../InjectAndCaptureDll/InjectAndCaptureDll.h"
#include "../InjectAndCaptureDll/MouseEvent.h"

int main()
{
	char a;
	puts("started");
	MakeWindow();
	puts("made window");
	std::cin >> a;
}
