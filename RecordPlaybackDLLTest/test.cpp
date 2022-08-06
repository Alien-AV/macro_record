#include "pch.h"
// #include "../RecordPlaybackDLL/Record/RecordEngine.h"
// #include "../RecordPlaybackDLL/Common/KeyboardEvent.h"
// #include "../RecordPlaybackDLL/Common/MouseEvent.h"

// TEST(RecordMouseEvents, ShouldRecordRelativeMouseMovement) {
// 	const record_playback::RecordEngine::record_events_callback_t cb = [](std::unique_ptr<Event> ev)
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
// 	const auto record_engine = std::make_unique<record_playback::RecordEngine>(cb, NULL);
//
// 	record_engine->start_record();
// 	record_engine->handle_mouse_event(data); // need to use FRIEND_TEST(RecordMouseEvents, ShouldRecordRelativeMouseMovement); in production code here =\
// 	data.lLastX = 1;
// 	data.usFlags = 0;
// 	record_engine->handle_mouse_event(data);
// 	data.lLastX = 0;
// 	record_engine->handle_mouse_event(data);
// 	data.lLastX = 2;
// 	record_engine->handle_mouse_event(data);
// 	data.lLastX = -1;
// 	record_engine->handle_mouse_event(data);
// }
//
// TEST(RecordMouseEvents, ShouldRecordAbsoluteMouseMovement) {
// 	const record_playback::RecordEngine::record_events_callback_t cb = [](std::unique_ptr<Event> ev)
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
// 	const auto record_engine = std::make_unique<record_playback::RecordEngine>(cb, NULL);
//
// 	record_engine->start_record();
// 	record_engine->handle_mouse_event(data);
// 	data.lLastX = 101;
// 	record_engine->handle_mouse_event(data);
// 	data.lLastX = 101;
// 	record_engine->handle_mouse_event(data);
// 	data.lLastX = 103;
// 	record_engine->handle_mouse_event(data);
// 	data.lLastX = 102;
// 	record_engine->handle_mouse_event(data);
// }