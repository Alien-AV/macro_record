#include "..\stdafx.h"
#include <memory>
#include <strsafe.h>
#include <algorithm>
#include <chrono>
#include "../RecordPlaybackDLL.h"
#include "../Common/Event.h"
#include "../Common/KeyboardEvent.h"
#include "../Common/MouseEvent.h"
#include "RecordEngine.h"

namespace record_playback {
	bool RecordEngine::register_raw_input_stuff(HWND hwnd)
	{
		RAWINPUTDEVICE rid[2];

		rid[0].usUsagePage = 0x01;
		rid[0].usUsage = 0x02;
		rid[0].dwFlags = RIDEV_INPUTSINK;
		rid[0].hwndTarget = hwnd;

		rid[1].usUsagePage = 0x01;
		rid[1].usUsage = 0x06;
		rid[1].dwFlags = RIDEV_INPUTSINK;
		rid[1].hwndTarget = hwnd;

		return RegisterRawInputDevices(rid, 2, sizeof rid[0]);
	}

	bool RecordEngine::unregister_raw_input_stuff()
	{
		RAWINPUTDEVICE rid[2];

		rid[0].usUsagePage = 0x01;
		rid[0].usUsage = 0x02;
		rid[0].dwFlags = RIDEV_REMOVE;
		rid[0].hwndTarget = nullptr;

		rid[1].usUsagePage = 0x01;
		rid[1].usUsage = 0x06;
		rid[1].dwFlags = RIDEV_REMOVE;
		rid[1].hwndTarget = nullptr;

		return RegisterRawInputDevices(rid, 2, sizeof rid[0]);
	}

	bool RecordEngine::save_engine_ptr_to_window(const HWND &hwnd, const LPARAM &createstruct_l_param)
	{
		auto engine_object_ptr = (reinterpret_cast<CREATESTRUCT*>(createstruct_l_param)->lpCreateParams);
		SetLastError(0);
		if (!SetWindowLongPtr(hwnd, GWLP_USERDATA, LONG_PTR(engine_object_ptr)))
		{
			if (GetLastError() != 0)
			{
				return false;
			}
		}
		return true;
	}

	bool RecordEngine::get_engine_ptr_from_window(const HWND &hwnd, record_playback::RecordEngine** record_engine_p)
	{
		SetLastError(0);
		const auto stored_ptr = GetWindowLongPtr(hwnd, GWLP_USERDATA);
		if(stored_ptr == 0 && GetLastError() != 0)
		{
			return false;
		}
		*record_engine_p = reinterpret_cast<RecordEngine*>(stored_ptr);
		return true;
	}

	LRESULT RecordEngine::recording_window_wnd_proc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
	{
		switch (message)
		{
		case WM_CREATE:
			if(!save_engine_ptr_to_window(hwnd, lParam)) //TODO: instead split to "get engine" and "save to window" (for sake of error reporting)
			{
				//TODO: report error and exit
				PostQuitMessage(0);
			}
			break;
		case WM_INPUT:
		{
			RecordEngine* engine_object;
			if(!get_engine_ptr_from_window(hwnd, &engine_object))
			{
				//TODO: report error and exit
				PostQuitMessage(0);
			}
			UINT dw_size;
			GetRawInputData(HRAWINPUT(lParam), RID_INPUT, nullptr, &dw_size, sizeof(RAWINPUTHEADER));
			const auto lpb = std::make_unique<BYTE[]>(dw_size);

			if (GetRawInputData(HRAWINPUT(lParam), RID_INPUT, lpb.get(), &dw_size, sizeof(RAWINPUTHEADER)) != dw_size){
				//TODO: report error and exit
				OutputDebugString(TEXT("GetRawInputData does not return correct size !\n"));
			}

			const auto raw_input = reinterpret_cast<RAWINPUT*>(lpb.get());

			if (raw_input->header.dwType == RIM_TYPEKEYBOARD)
			{
				engine_object->handle_keyboard_event(raw_input->data.keyboard);
			}
			else if (raw_input->header.dwType == RIM_TYPEMOUSE)
			{
				engine_object->handle_mouse_event(raw_input->data.mouse);
			}
			break;
		}
		case WM_DESTROY:
			PostQuitMessage(0);
			break;
		default:
			return DefWindowProc(hwnd, message, wParam, lParam);
		}
		return 0;
	}

	bool RecordEngine::register_window_class_if_needed(const wchar_t* const class_name)
	{
		WNDCLASSEX wx = {};
		wx.cbSize = sizeof(WNDCLASSEX);
		wx.lpfnWndProc = recording_window_wnd_proc;
		wx.lpszClassName = class_name;

		return RegisterClassEx(&wx) || GetLastError() == ERROR_CLASS_ALREADY_EXISTS;
	}

	DWORD RecordEngine::recording_window_main_loop_thread(LPVOID engine_object_void_p)
	{
		MSG messages;
		HWND hwnd = nullptr;
		auto engine_object = static_cast<RecordEngine*>(engine_object_void_p);

		const auto class_name = L"RECORD_PLAYBACK_DLL_WINDOW_CLASS";
		if(!register_window_class_if_needed(class_name))
		{
			//TODO: report error and exit
			return 1;
		}
		hwnd = CreateWindowEx(0, class_name, L"RecordPlaybackDLL", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, nullptr, engine_object);
		if (!hwnd) {
			//TODO: report error and exit
			return 1;
		}

		while (GetMessage(&messages, nullptr, 0, 0))
		{
			const auto time_start = std::chrono::steady_clock::now();
			switch (messages.message) {
			case WM_START_RECORD:
				engine_object->time_of_start_of_recording_ = std::chrono::high_resolution_clock::now();
				engine_object->fake_mouse_event_for_initial_pos();

				if(!engine_object->register_raw_input_stuff(hwnd))
				{
					//TODO: report error and exit
				}
				break;
			case WM_STOP_RECORD:
				if(!engine_object->unregister_raw_input_stuff())
				{
					//TODO: report error and exit
				}
				break;
			default:
				TranslateMessage(&messages);
				DispatchMessage(&messages);
			}
		}
		return 1;
	}

	void RecordEngine::event_fast_collector_thread_method()
	{
		while(true){
			if(event_fast_collector_thread_should_close_) return;
			std::this_thread::sleep_for(std::chrono::milliseconds(100));
			{
				std::lock_guard<std::mutex> lock(*fast_collect_event_queue_mt_);
				std::swap(fast_collect_events_queue_, collected_events_further_processing_queue_);
			}
			while(!collected_events_further_processing_queue_->empty())
			{
				auto event = std::move(collected_events_further_processing_queue_->front());
				collected_events_further_processing_queue_->pop();
				record_events_callback_(std::move(event));
			}
		}
	}

	void RecordEngine::handle_keyboard_event(const RAWKEYBOARD data) const
	{
		const auto time_since_start_of_recording = std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::high_resolution_clock::now() - time_of_start_of_recording_);
		auto keyboard_event = std::make_unique<KeyboardEvent>();
		keyboard_event->time_since_start_of_recording = time_since_start_of_recording;
		keyboard_event->virtualKeyCode = data.VKey;
		if (data.Flags & RI_KEY_BREAK) {
			keyboard_event->keyUp = true;
		}
		else {
			keyboard_event->keyUp = false;
		}

		process_recorded_event(std::move(keyboard_event));
	}

	void RecordEngine::handle_mouse_event(RAWMOUSE data) const
	{
		const auto time_since_start_of_recording = std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::high_resolution_clock::now() - time_of_start_of_recording_);

		auto mouse_event = std::make_unique<MouseEvent>();
		mouse_event->time_since_start_of_recording = time_since_start_of_recording;
		mouse_event->mappedToVirtualDesktop = (data.usFlags & MOUSE_VIRTUAL_DESKTOP);

		mouse_event->relative_position = !(data.usFlags & MOUSE_MOVE_ABSOLUTE);

		mouse_event->x = data.lLastX;
		mouse_event->y = data.lLastY;

		mouse_event->ActionType |= MouseEvent::ActionTypeFlags::Move;
		//TODO: rewrite to a prettier, more robust mapping. a million if statements is not our way
		if (data.usButtonFlags & RI_MOUSE_WHEEL) {
			mouse_event->wheelRotation = data.usButtonData;
		}
		if (data.usButtonFlags & RI_MOUSE_LEFT_BUTTON_DOWN) {
			mouse_event->ActionType |= MouseEvent::ActionTypeFlags::LeftDown;
		}
		if (data.usButtonFlags & RI_MOUSE_LEFT_BUTTON_UP) {
			mouse_event->ActionType |= MouseEvent::ActionTypeFlags::LeftUp;
		}
		if (data.usButtonFlags & RI_MOUSE_MIDDLE_BUTTON_DOWN) {
			mouse_event->ActionType |= MouseEvent::ActionTypeFlags::MiddleDown;
		}
		if (data.usButtonFlags & RI_MOUSE_MIDDLE_BUTTON_UP) {
			mouse_event->ActionType |= MouseEvent::ActionTypeFlags::MiddleUp;
		}
		if (data.usButtonFlags & RI_MOUSE_RIGHT_BUTTON_DOWN) {
			mouse_event->ActionType |= MouseEvent::ActionTypeFlags::RightDown;
		}
		if (data.usButtonFlags & RI_MOUSE_RIGHT_BUTTON_UP) {
			mouse_event->ActionType |= MouseEvent::ActionTypeFlags::RightUp;
		}
		if (data.usButtonFlags & RI_MOUSE_BUTTON_4_DOWN) {
			mouse_event->ActionType |= MouseEvent::ActionTypeFlags::XDown;
		}
		if (data.usButtonFlags & RI_MOUSE_BUTTON_4_UP) {
			mouse_event->ActionType |= MouseEvent::ActionTypeFlags::XUp;
		}

		//TODO: x2 button

		process_recorded_event(std::move(mouse_event));
	}

	void RecordEngine::process_recorded_event(std::unique_ptr<Event> event) const
	{
		std::lock_guard<std::mutex> lock(*fast_collect_event_queue_mt_);
		fast_collect_events_queue_->push(std::move(event));
	}

	void RecordEngine::fake_mouse_event_for_initial_pos() const
	{
		POINT initial_mouse_position;
		GetCursorPos(&initial_mouse_position);

		auto fake_mouse_event = std::make_unique<MouseEvent>();
		fake_mouse_event->time_since_start_of_recording = std::chrono::microseconds(0);
		fake_mouse_event->x = initial_mouse_position.x;
		fake_mouse_event->y = initial_mouse_position.y;
		fake_mouse_event->ActionType = MouseEvent::ActionTypeFlags::Move;
		process_recorded_event(std::move(fake_mouse_event));
	}

	RecordEngine::RecordEngine(const record_events_callback_t record_events_cb, const status_callback_t status_cb) :
								record_events_callback_(record_events_cb), status_callback_(status_cb)
	{
		window_thread_id_ = std::make_unique<DWORD>(0);
		fast_collect_event_queue_mt_ = std::make_unique<std::mutex>();
		fast_collect_events_queue_ = std::make_unique<std::queue<std::unique_ptr<Event>>>();
		collected_events_further_processing_queue_ = std::make_unique<std::queue<std::unique_ptr<Event>>>();
		CreateThread(nullptr, NULL, recording_window_main_loop_thread, LPVOID(this), NULL, window_thread_id_.get());

		event_fast_collector_thread_ = std::thread{&RecordEngine::event_fast_collector_thread_method, this};
		
		//TODO: wait for a notification from the record window that the init (thread creation, window creation) is finished
	}

	RecordEngine::~RecordEngine()
	{
		if(!window_thread_id_)
		{
			return;
		}
		event_fast_collector_thread_should_close_ = true;
		event_fast_collector_thread_.join();

		PostThreadMessage(*window_thread_id_, WM_STOP_RECORD, NULL, NULL);	// can't call the stop_record method, or too many things happen (like status_callback_ being called while managed is dying)
		PostThreadMessage(*window_thread_id_, WM_CLOSE, NULL, NULL);		// do I even need to call "stop_record"?
		//TODO: verify that this indeed closes the bg window
		//TODO: wait here until the window is really closed?
		//TODO: unregister window class? (MSDN: No window classes registered by a DLL are unregistered when the DLL is unloaded)
	}

	void RecordEngine::start_record() const
	{
		if(PostThreadMessage(*window_thread_id_, WM_START_RECORD, NULL, NULL) == FALSE)
		{
			//TODO: report error and exit
		}
	}

	void RecordEngine::stop_record() const
	{
		if(PostThreadMessage(*window_thread_id_, WM_STOP_RECORD, NULL, NULL) == FALSE)
		{
			//TODO: report error and exit
		}
	}
}