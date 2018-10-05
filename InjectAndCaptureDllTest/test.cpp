#include "pch.h"
#include "../InjectAndCaptureDll/InjectAndCaptureDll.h"
#include "../InjectAndCaptureDll/Common/KeyboardEvent.h"
#include "../InjectAndCaptureDll/Common/MouseEvent.h"

// TEST(CaptureMouseEvents, ShouldCaptureRelativeMouseMovement) {
// 	const iac_dll::capture_events_callback_t cb = [](std::unique_ptr<Event> ev)
// 	{
// 		static u_int test_no = 0;
// 		std::cout << "test_no:" << test_no << std::endl;
// 		switch (test_no)
// 		{
// 		case 0:
// 			EXPECT_EQ(100,reinterpret_cast<MouseEvent *>(ev.get())->x);
// 			break;
// 		case 1:
// 			EXPECT_EQ(1,reinterpret_cast<MouseEvent *>(ev.get())->x);
// 			break;
// 		case 2:
// 			EXPECT_EQ(0,reinterpret_cast<MouseEvent *>(ev.get())->x);
// 			break;
// 		case 3:
// 			EXPECT_EQ(2,reinterpret_cast<MouseEvent *>(ev.get())->x);
// 			break;
// 		case 4:
// 			EXPECT_EQ(-1,reinterpret_cast<MouseEvent *>(ev.get())->x);
// 			break;
// 		default:
// 			FAIL();
// 		}
// 		test_no++;
// 	};
// 	RAWMOUSE data{};
// 	data.lLastX = 100;
// 	data.usFlags = MOUSE_MOVE_ABSOLUTE;
// 	
// 	iac_dll::StartCapture(cb);
// 	iac_dll::HandleMouseEventCapture(data);
// 	data.lLastX = 1;
// 	data.usFlags = 0;
// 	iac_dll::HandleMouseEventCapture(data);
// 	data.lLastX = 0;
// 	iac_dll::HandleMouseEventCapture(data);
// 	data.lLastX = 2;
// 	iac_dll::HandleMouseEventCapture(data);
// 	data.lLastX = -1;
// 	iac_dll::HandleMouseEventCapture(data);
// }
//
// TEST(CaptureMouseEvents, ShouldCaptureAbsoluteMouseMovement) {
// 	const iac_dll::capture_events_callback_t cb = [](std::unique_ptr<Event> ev)
// 	{
// 		static u_int test_no = 0;
// 		std::cout << "test_no:" << test_no << std::endl;
// 		switch (test_no)
// 		{
// 		case 0:
// 			EXPECT_EQ(100,reinterpret_cast<MouseEvent *>(ev.get())->x);
// 			break;
// 		case 1:
// 			EXPECT_EQ(101,reinterpret_cast<MouseEvent *>(ev.get())->x);
// 			break;
// 		case 2:
// 			EXPECT_EQ(101,reinterpret_cast<MouseEvent *>(ev.get())->x);
// 			break;
// 		case 3:
// 			EXPECT_EQ(103,reinterpret_cast<MouseEvent *>(ev.get())->x);
// 			break;
// 		case 4:
// 			EXPECT_EQ(102,reinterpret_cast<MouseEvent *>(ev.get())->x);
// 			break;
// 		default:
// 			FAIL();
// 		}
// 		test_no++;
// 	};
// 	RAWMOUSE data{};
// 	data.lLastX = 100;
// 	data.usFlags = MOUSE_MOVE_ABSOLUTE;
// 	
// 	iac_dll::StartCapture(cb);
// 	iac_dll::HandleMouseEventCapture(data);
// 	data.lLastX = 101;
// 	iac_dll::HandleMouseEventCapture(data);
// 	data.lLastX = 101;
// 	iac_dll::HandleMouseEventCapture(data);
// 	data.lLastX = 103;
// 	iac_dll::HandleMouseEventCapture(data);
// 	data.lLastX = 102;
// 	iac_dll::HandleMouseEventCapture(data);
// }