#pragma once
#include <queue>
#include "../Common/Event.h"
#include "../../Common/StatusEnum.cs"

namespace record_playback
{
	class RecordEngine
	{
	public:
		using status_callback_t = void(*)(RecordPlaybackDLLEnums::StatusCode);
		using record_events_callback_t = void(*)(std::unique_ptr<Event>);
		
		RecordEngine(record_events_callback_t, status_callback_t);
		~RecordEngine();
		
		RecordEngine(RecordEngine&& other) noexcept = default;
		RecordEngine(RecordEngine& other) = delete;
		RecordEngine& operator=(RecordEngine&& other) noexcept = default;
		RecordEngine& operator=(const RecordEngine& other) = delete;
		
		void start_record() const;
		void stop_record() const;

	private:
		const static UINT WM_START_RECORD = WM_USER;
		const static UINT WM_STOP_RECORD = WM_USER + 1;

		record_events_callback_t record_events_callback_ = nullptr;
		status_callback_t status_callback_ = nullptr;

		std::chrono::time_point<std::chrono::high_resolution_clock> time_of_start_of_recording_{};

		std::unique_ptr<DWORD> window_thread_id_ = nullptr;

		std::unique_ptr<std::mutex> fast_collect_event_queue_mt_;
		std::unique_ptr<std::queue<std::unique_ptr<Event>>> fast_collect_events_queue_;
		std::unique_ptr<std::queue<std::unique_ptr<Event>>> collected_events_further_processing_queue_;

		static bool register_raw_input_stuff(HWND hwnd);
		static bool unregister_raw_input_stuff();
		static bool save_engine_ptr_to_window(const HWND&, const LPARAM&);
		static bool get_engine_ptr_from_window(const HWND&, record_playback::RecordEngine**);
		static LRESULT CALLBACK recording_window_wnd_proc(HWND, UINT, WPARAM, LPARAM);
		static bool register_window_class_if_needed(const wchar_t* class_name);
		static DWORD WINAPI recording_window_main_loop_thread(LPVOID);
		void event_fast_collector_thread_method();
		std::thread event_fast_collector_thread_;
		bool event_fast_collector_thread_should_close_ = false;

		void handle_keyboard_event(const RAWKEYBOARD& data) const;
		void handle_mouse_event(const RAWMOUSE& data) const;
		void handle_controller_event(const RAWHID& hid);
		void process_recorded_event(std::unique_ptr<Event> event) const;
		void fake_mouse_event_for_initial_pos() const;
	};

}
