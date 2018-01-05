// macro_record.cpp : Defines the entry point for the console application.
//

#include "stdafx.h"
#include <fstream>
#include "../InjectAndCaptureDll/InjectAndCaptureDll.h"
#include <conio.h>

int main()
{
	char a;
	puts("started");
	Init();
	puts("initialized.\npress a key to start capturing");
	_getch();
	bool res = StartCapture();
	printf("started capturing, retval: %d.\npress a key to stop capturing\n", res);
	_getch();
	auto resultList = StopCapture();
	printf("stopped capturing, printing all results:\n");
	for (auto event : (*resultList)) {
		event;
	}
	_getch();
}
