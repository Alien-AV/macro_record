#pragma once
#include <deque>
#include <future>
#include <thread>
#include "RecordingPipeline.h"
#include "../../Common/StatusEnum.cs"

namespace record_playback {
class RecordEngine {
public:
    using status_callback_t = void(*)(RecordPlaybackDLLEnums::StatusCode);
    using record_events_callback_t = void(*)(std::unique_ptr<Event>, uint64_t);
    using boundary_callback_t = void(*)(uint64_t, capture::Boundary, uint32_t, uint32_t, capture::PointerOrigin);

    RecordEngine(record_events_callback_t, status_callback_t, boundary_callback_t);
    ~RecordEngine();
    RecordEngine(const RecordEngine&) = delete;
    RecordEngine& operator=(const RecordEngine&) = delete;
    bool ready() const { return ready_; }
    bool start_record(uint64_t session_id, uint32_t stop_gestures) const;
    bool stop_record(uint64_t session_id, uint32_t gesture, DWORD message_time) const;

private:
    static constexpr UINT WM_START_RECORD = WM_APP + 1;
    static constexpr UINT WM_STOP_RECORD = WM_APP + 2;
    static constexpr UINT WM_FAILED_RECORD = WM_APP + 3;
    static constexpr UINT WM_SHUTDOWN_RECORD = WM_APP + 4;
    struct Command { UINT kind; uint64_t session; DWORD cutoff; uint32_t gestures; };

    record_events_callback_t record_events_callback_;
    status_callback_t status_callback_;
    boundary_callback_t boundary_callback_;
    capture::Pipeline pipeline_;
    std::deque<Command> commands_;
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
    void read_input(HRAWINPUT);
};
}
