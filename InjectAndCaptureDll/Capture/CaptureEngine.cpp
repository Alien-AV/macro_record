#include "..\stdafx.h"
#include <memory>
#include <strsafe.h>
#include <algorithm>
#include <chrono>
#include "../InjectAndCaptureDll.h"
#include "../Common/Event.h"
#include "../Common/KeyboardEvent.h"
#include "../Common/MouseEvent.h"
#include "CaptureEngine.h"

namespace iac_dll {
	bool CaptureEngine::register_raw_input_stuff(HWND hwnd)
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

	bool CaptureEngine::unregister_raw_input_stuff()
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

	bool CaptureEngine::save_engine_ptr_to_window(const HWND &hwnd, const LPARAM &createstruct_l_param)
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

	bool CaptureEngine::get_engine_ptr_from_window(const HWND &hwnd, iac_dll::CaptureEngine** capture_engine_p)
	{
		SetLastError(0);
		const auto stored_ptr = GetWindowLongPtr(hwnd, GWLP_USERDATA);
		if(stored_ptr == 0 && GetLastError() != 0)
		{
			return false;
		}
		*capture_engine_p = reinterpret_cast<CaptureEngine*>(stored_ptr);
		return true;
	}

	LRESULT CaptureEngine::capture_window_wnd_proc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
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
			CaptureEngine* engine_object;
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
				engine_object->handle_keyboard_event_capture(raw_input->data.keyboard);
			}
			else if (raw_input->header.dwType == RIM_TYPEMOUSE)
			{
				engine_object->handle_mouse_event_capture(raw_input->data.mouse);
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

	bool CaptureEngine::register_window_class_if_needed(const wchar_t* const class_name)
	{
		WNDCLASSEX wx = {};
		wx.cbSize = sizeof(WNDCLASSEX);
		wx.lpfnWndProc = capture_window_wnd_proc;
		wx.lpszClassName = class_name;

		return RegisterClassEx(&wx) || GetLastError() == ERROR_CLASS_ALREADY_EXISTS;
	}

	DWORD CaptureEngine::capture_window_main_loop_thread(LPVOID engine_object_void_p)
	{
		MSG messages;
		HWND hwnd = nullptr;
		auto engine_object = static_cast<CaptureEngine*>(engine_object_void_p);

		const auto class_name = L"INJECT_AND_CAPTURE_DLL_WINDOW_CLASS";
		if(!register_window_class_if_needed(class_name))
		{
			//TODO: report error and exit
			return 1;
		}
		hwnd = CreateWindowEx(0, class_name, L"inject_and_capture_dll", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, nullptr, engine_object);
		if (!hwnd) {
			//TODO: report error and exit
			return 1;
		}

		while (GetMessage(&messages, nullptr, 0, 0))
		{
			const auto time_start = std::chrono::steady_clock::now();
			switch (messages.message) {
			case WM_STARTCAPTURE:
				engine_object->time_of_start_of_recording_ = std::chrono::high_resolution_clock::now();
				engine_object->fake_mouse_event_for_initial_pos();

				if(!engine_object->register_raw_input_stuff(hwnd))
				{
					//TODO: report error and exit
				}
				break;
			case WM_STOPCAPTURE:
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

	void CaptureEngine::event_fast_collector_thread_method()
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
				capture_events_callback_(std::move(event));
			}
		}
	}

	void CaptureEngine::handle_keyboard_event_capture(const RAWKEYBOARD data) const
	{
		const auto time_since_start_of_recording = std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::high_resolution_clock::now() - time_of_start_of_recording_);
		auto captured_kbd_event = std::make_unique<KeyboardEvent>();
		captured_kbd_event->time_since_start_of_recording = time_since_start_of_recording;
		captured_kbd_event->virtualKeyCode = data.VKey;
		if (data.Flags & RI_KEY_BREAK) {
			captured_kbd_event->keyUp = true;
		}
		else {
			captured_kbd_event->keyUp = false;
		}

		process_captured_event(std::move(captured_kbd_event));
	}

	void CaptureEngine::handle_mouse_event_capture(RAWMOUSE data) const
	{
		const auto time_since_start_of_recording = std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::high_resolution_clock::now() - time_of_start_of_recording_);

		auto captured_mouse_event = std::make_unique<MouseEvent>();
		captured_mouse_event->time_since_start_of_recording = time_since_start_of_recording;
		captured_mouse_event->mappedToVirtualDesktop = (data.usFlags & MOUSE_VIRTUAL_DESKTOP);

		captured_mouse_event->relative_position = !(data.usFlags & MOUSE_MOVE_ABSOLUTE);

		captured_mouse_event->x = data.lLastX;
		captured_mouse_event->y = data.lLastY;

		captured_mouse_event->ActionType |= MouseEvent::ActionTypeFlags::Move;

		if (data.usButtonFlags & RI_MOUSE_WHEEL) {
			captured_mouse_event->wheelRotation = data.usButtonData;
		}

		if (data.usButtonFlags & RI_MOUSE_LEFT_BUTTON_DOWN) {
			captured_mouse_event->ActionType |= MouseEvent::ActionTypeFlags::LeftDown;
		}

		if (data.usButtonFlags & RI_MOUSE_LEFT_BUTTON_UP) {
			captured_mouse_event->ActionType |= MouseEvent::ActionTypeFlags::LeftUp;
		}
		if (data.usButtonFlags & RI_MOUSE_MIDDLE_BUTTON_DOWN) {
			captured_mouse_event->ActionType |= MouseEvent::ActionTypeFlags::MiddleDown;
		}

		if (data.usButtonFlags & RI_MOUSE_MIDDLE_BUTTON_UP) {
			captured_mouse_event->ActionType |= MouseEvent::ActionTypeFlags::MiddleUp;
		}

		if (data.usButtonFlags & RI_MOUSE_RIGHT_BUTTON_DOWN) {
			captured_mouse_event->ActionType |= MouseEvent::ActionTypeFlags::RightDown;
		}

		if (data.usButtonFlags & RI_MOUSE_RIGHT_BUTTON_UP) {
			captured_mouse_event->ActionType |= MouseEvent::ActionTypeFlags::RightUp;
		}

		if (data.usButtonFlags & RI_MOUSE_BUTTON_4_DOWN) {
			captured_mouse_event->ActionType |= MouseEvent::ActionTypeFlags::XDown;
		}

		if (data.usButtonFlags & RI_MOUSE_BUTTON_4_UP) {
			captured_mouse_event->ActionType |= MouseEvent::ActionTypeFlags::XUp;
		}

		//TODO: x2 button

		process_captured_event(std::move(captured_mouse_event));
	}

	void CaptureEngine::process_captured_event(std::unique_ptr<Event> event) const
	{
		std::lock_guard<std::mutex> lock(*fast_collect_event_queue_mt_);
		fast_collect_events_queue_->push(std::move(event));
	}

	void CaptureEngine::fake_mouse_event_for_initial_pos() const
	{
		POINT initial_mouse_position;
		GetCursorPos(&initial_mouse_position);

		auto fake_mouse_event = std::make_unique<MouseEvent>();
		fake_mouse_event->time_since_start_of_recording = std::chrono::microseconds(0);
		fake_mouse_event->x = initial_mouse_position.x;
		fake_mouse_event->y = initial_mouse_position.y;
		fake_mouse_event->ActionType = MouseEvent::ActionTypeFlags::Move;
		process_captured_event(std::move(fake_mouse_event));
	}

	CaptureEngine::CaptureEngine(capture_events_callback_t capture_events_cb, error_callback_t error_cb) :
								capture_events_callback_(capture_events_cb), error_callback_(error_cb)
	{
		window_thread_id_ = std::make_unique<DWORD>(0);
		fast_collect_event_queue_mt_ = std::make_unique<std::mutex>();
		fast_collect_events_queue_ = std::make_unique<std::queue<std::unique_ptr<Event>>>();
		collected_events_further_processing_queue_ = std::make_unique<std::queue<std::unique_ptr<Event>>>();
		CreateThread(nullptr, NULL, capture_window_main_loop_thread, LPVOID(this), NULL, window_thread_id_.get());

		event_fast_collector_thread_ = std::thread{&CaptureEngine::event_fast_collector_thread_method, this};
		
		//TODO: wait for a notification from the capture window that the init (thread creation, window creation) is finished
	}

	CaptureEngine::~CaptureEngine()
	{
		if(!window_thread_id_)
		{
			return;
		}
		event_fast_collector_thread_should_close_ = true;
		event_fast_collector_thread_.join();

		PostThreadMessage(*window_thread_id_, WM_STOPCAPTURE, NULL, NULL); // if calling stop_capture, too many things happen (like error_callback_ being called while managed is dying)
		PostThreadMessage(*window_thread_id_, WM_CLOSE, NULL, NULL);		// do I even need to call "stopcapture"?
		//TODO: verify that this indeed closes the bg window
		//TODO: wait here until the window is really closed?
		//TODO: unregister window class? (MSDN: No window classes registered by a DLL are unregistered when the DLL is unloaded)
	}

	void CaptureEngine::start_capture() const
	{
		if(PostThreadMessage(*window_thread_id_, WM_STARTCAPTURE, NULL, NULL) == FALSE)
		{
			//TODO: report error and exit
		}
	}

	void CaptureEngine::stop_capture()
	{
		if(PostThreadMessage(*window_thread_id_, WM_STOPCAPTURE, NULL, NULL) == FALSE)
		{
			//TODO: report error and exit
		}
	}
}