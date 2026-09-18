#pragma once
#include <condition_variable>
#include <deque>
#include <future>
#include <mutex>
#include <queue>
#include <thread>
#include "RecordingStream.h"
#include "../../Common/StatusEnum.cs"

namespace record_playback {
class RecordEngine {
public:
    using status_callback_t = void(*)(RecordPlaybackDLLEnums::StatusCode);
    using record_events_callback_t = void(*)(std::unique_ptr<Event>, uint64_t);
    using boundary_callback_t = void(*)(uint64_t, capture::Boundary, uint32_t, uint32_t);

    RecordEngine(record_events_callback_t, status_callback_t, boundary_callback_t);
    ~RecordEngine();
    RecordEngine(const RecordEngine&) = delete;
    RecordEngine& operator=(const RecordEngine&) = delete;
    bool ready() const { return ready_; }
    bool start_record(uint64_t session_id) const;
    bool stop_record(uint64_t session_id) const;

private:
    static constexpr UINT WM_START_RECORD = WM_APP + 1;
    static constexpr UINT WM_STOP_RECORD = WM_APP + 2;
    static constexpr UINT WM_SHUTDOWN_RECORD = WM_APP + 4;
    struct Command { UINT kind; uint64_t session; DWORD cutoff; };

    record_events_callback_t record_events_callback_;
    status_callback_t status_callback_;
    boundary_callback_t boundary_callback_;
    std::mutex queue_mutex_;
    std::condition_variable queue_changed_;
    std::queue<capture::Packet> queue_;
    bool collector_closing_ = false;
    capture::Stream stream_;
    std::deque<Command> commands_;
    std::chrono::steady_clock::time_point time_of_last_event_{};
    std::thread window_thread_;
    std::thread collector_thread_;
    DWORD window_thread_id_ = 0;
    bool ready_ = false;

    static bool register_raw_input_stuff(HWND);
    static bool unregister_raw_input_stuff();
    static LRESULT CALLBACK recording_window_wnd_proc(HWND, UINT, WPARAM, LPARAM);
    void window_main(std::promise<bool>);
    void advance_boundaries(HWND);
    void collect();
    void enqueue(capture::Packet);
    void read_input(HRAWINPUT);
    std::chrono::microseconds get_time_since_last_event();
    void handle_keyboard_event(const RAWKEYBOARD&);
    void handle_mouse_event(const RAWMOUSE&);
    void process_recorded_event(std::unique_ptr<Event>);
    void fake_mouse_event_for_initial_pos();
};
}
