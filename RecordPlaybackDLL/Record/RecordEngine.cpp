#include "../stdafx.h"
#include "RecordEngine.h"
#include "../Common/KeyboardEvent.h"
#include "../Common/MouseEvent.h"
#include "../Common/MouseTranslation.h"
#include <vector>

namespace record_playback {
bool RecordEngine::register_raw_input_stuff(HWND hwnd) {
    RAWINPUTDEVICE devices[] = {{0x01, 0x02, RIDEV_INPUTSINK, hwnd}, {0x01, 0x06, RIDEV_INPUTSINK, hwnd}};
    return RegisterRawInputDevices(devices, 2, sizeof(RAWINPUTDEVICE)) != FALSE;
}

bool RecordEngine::unregister_raw_input_stuff() {
    RAWINPUTDEVICE devices[] = {{0x01, 0x02, RIDEV_REMOVE, nullptr}, {0x01, 0x06, RIDEV_REMOVE, nullptr}};
    return RegisterRawInputDevices(devices, 2, sizeof(RAWINPUTDEVICE)) != FALSE;
}

LRESULT CALLBACK RecordEngine::recording_window_wnd_proc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam) {
    if (message == WM_NCCREATE) {
        const auto creation = reinterpret_cast<CREATESTRUCT*>(lParam);
        SetWindowLongPtr(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(creation->lpCreateParams));
    } else if (message == WM_INPUT) {
        const auto engine = reinterpret_cast<RecordEngine*>(GetWindowLongPtr(hwnd, GWLP_USERDATA));
        if (engine) {
            try { engine->read_input(reinterpret_cast<HRAWINPUT>(lParam)); }
            catch (...) {
                if (engine->status_callback_) engine->status_callback_(RecordPlaybackDLLEnums::ErrorCouldNotProcessInputData);
            }
        }
        // Raw Input cleanup (in particular RIM_INPUT) must run even when not recording.
        return DefWindowProc(hwnd, message, wParam, lParam);
    } else if (message == WM_DESTROY) {
        PostQuitMessage(0);
    }
    return DefWindowProc(hwnd, message, wParam, lParam);
}

void RecordEngine::read_input(HRAWINPUT handle) {
    UINT size = 0;
    if (GetRawInputData(handle, RID_INPUT, nullptr, &size, sizeof(RAWINPUTHEADER)) == UINT(-1)
        || size < sizeof(RAWINPUTHEADER)) return;
    std::vector<BYTE> bytes(size);
    if (GetRawInputData(handle, RID_INPUT, bytes.data(), &size, sizeof(RAWINPUTHEADER)) != size) return;
    const auto raw = reinterpret_cast<const RAWINPUT*>(bytes.data());
    if (raw->header.dwType == RIM_TYPEKEYBOARD) handle_keyboard_event(raw->data.keyboard);
    else if (raw->header.dwType == RIM_TYPEMOUSE && stream_.session()) handle_mouse_event(raw->data.mouse);
}

void RecordEngine::window_main(std::promise<bool> initialized) {
    window_thread_id_ = GetCurrentThreadId();
    const auto class_name = L"RECORD_PLAYBACK_DLL_WINDOW_CLASS";
    WNDCLASSEX window_class{};
    window_class.cbSize = sizeof(window_class);
    window_class.lpfnWndProc = recording_window_wnd_proc;
    window_class.lpszClassName = class_name;
    if (!RegisterClassEx(&window_class) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) {
        initialized.set_value(false);
        return;
    }
    const auto hwnd = CreateWindowEx(0, class_name, L"RecordPlaybackDLL", 0, 0, 0, 0, 0,
        HWND_MESSAGE, nullptr, nullptr, this);
    if (!hwnd || !register_raw_input_stuff(hwnd)) {
        if (hwnd) DestroyWindow(hwnd);
        initialized.set_value(false);
        return;
    }

    // Registration stays active until shutdown. Held state comes only from ordered
    // raw transitions, never an asynchronous snapshot taken beside a nonempty queue.
    initialized.set_value(true);
    MSG message{};
    for (;;) {
        if (!commands_.empty()) advance_boundaries(hwnd);
        if (!commands_.empty()) {
            // Service control messages between bounded raw batches without posting
            // extra continuation messages or waiting for an ever-busy input queue.
            if (!PeekMessage(&message, nullptr, WM_START_RECORD, WM_SHUTDOWN_RECORD, PM_REMOVE)) continue;
        } else if (GetMessage(&message, nullptr, 0, 0) <= 0) break;
        if (message.message == WM_SHUTDOWN_RECORD || message.message == WM_QUIT) break;
        if (message.message == WM_START_RECORD || message.message == WM_STOP_RECORD) {
            commands_.push_back({message.message, static_cast<uint64_t>(message.wParam), message.time});
        } else {
            TranslateMessage(&message);
            DispatchMessage(&message);
        }
    }
    if (stream_.session()) stream_.stop(stream_.session());
    unregister_raw_input_stuff();
    DestroyWindow(hwnd);
}

void RecordEngine::advance_boundaries(HWND hwnd) {
    if (commands_.empty()) return;
    const auto command = commands_.front();
    MSG raw{};
    const auto drained = capture::drain_prefix(command.cutoff,
        [&](DWORD& time) {
            if (!PeekMessage(&raw, hwnd, WM_INPUT, WM_INPUT, PM_NOREMOVE)) return false;
            time = raw.time;
            return true;
        }, [&] {
            if (PeekMessage(&raw, hwnd, WM_INPUT, WM_INPUT, PM_REMOVE)) DispatchMessage(&raw);
        });
    if (drained) {
        commands_.pop_front();
        if (command.kind == WM_START_RECORD) {
            if (stream_.start(command.session)) {
                time_of_last_event_ = std::chrono::steady_clock::now();
                fake_mouse_event_for_initial_pos();
            }
        } else {
            stream_.stop(command.session);
        }
    }
    // At most 256 raw messages per turn. The fixed cutoff excludes future input,
    // so a sustained device stream cannot continually extend the prefix to drain.
}

void RecordEngine::enqueue(capture::Packet packet) {
    {
        std::lock_guard<std::mutex> lock(queue_mutex_);
        queue_.push(std::move(packet));
    }
    queue_changed_.notify_one();
}

void RecordEngine::collect() {
    for (;;) {
        capture::Packet packet;
        {
            std::unique_lock<std::mutex> lock(queue_mutex_);
            queue_changed_.wait(lock, [&] { return collector_closing_ || !queue_.empty(); });
            if (queue_.empty()) return;
            packet = std::move(queue_.front());
            queue_.pop();
        }
        if (packet.event) record_events_callback_(std::move(packet.event), packet.session);
        else boundary_callback_(packet.session, packet.boundary, packet.held_keys, packet.idle_released_keys);
    }
}

std::chrono::microseconds RecordEngine::get_time_since_last_event() {
    const auto now = std::chrono::steady_clock::now();
    const auto elapsed = std::chrono::duration_cast<std::chrono::microseconds>(now - time_of_last_event_);
    time_of_last_event_ = now;
    return elapsed;
}

void RecordEngine::handle_keyboard_event(const RAWKEYBOARD& data) {
    if (data.VKey == 255) return;
    const auto key = capture::sided_key(data);
    const auto up = (data.Flags & RI_KEY_BREAK) != 0;
    stream_.key(key, up);
    if (!stream_.session()) return;
    auto event = std::make_unique<KeyboardEvent>();
    event->time_since_last_event = get_time_since_last_event();
    event->virtualKeyCode = key;
    event->keyUp = up;
    process_recorded_event(std::move(event));
}

void RecordEngine::handle_mouse_event(const RAWMOUSE& data) {
    const auto bounds = (data.usFlags & MOUSE_MOVE_ABSOLUTE)
        ? mouse::physical_desktop_bounds((data.usFlags & MOUSE_VIRTUAL_DESKTOP) != 0) : mouse::DesktopBounds{};
    auto actions = mouse::translate_raw_mouse(data, bounds, std::chrono::microseconds(0));
    if (actions.empty()) return;
    actions.front().delay = get_time_since_last_event();
    for (const auto& action : actions) {
        auto mouse_event = std::make_unique<MouseEvent>(action.x, action.y, action.flags,
            action.data, action.virtual_desktop, action.relative);
        mouse_event->time_since_last_event = action.delay;
        process_recorded_event(std::move(mouse_event));
    }
}

void RecordEngine::process_recorded_event(std::unique_ptr<Event> event) {
    stream_.input(std::move(event));
}

void RecordEngine::fake_mouse_event_for_initial_pos() {
    POINT initial_mouse_position{};
    if (!GetPhysicalCursorPos(&initial_mouse_position)) return;
    auto event = std::make_unique<MouseEvent>();
    event->time_since_last_event = std::chrono::microseconds(0);
    event->x = initial_mouse_position.x;
    event->y = initial_mouse_position.y;
    event->ActionType = MouseEvent::ActionTypeFlags::Move;
    event->mappedToVirtualDesktop = true;
    process_recorded_event(std::move(event));
}

RecordEngine::RecordEngine(record_events_callback_t input, status_callback_t status, boundary_callback_t boundary)
    : record_events_callback_(input), status_callback_(status), boundary_callback_(boundary),
      stream_([this](capture::Packet packet) { enqueue(std::move(packet)); }) {
    std::promise<bool> initialized;
    auto readiness = initialized.get_future();
    collector_thread_ = std::thread(&RecordEngine::collect, this);
    try {
        window_thread_ = std::thread(&RecordEngine::window_main, this, std::move(initialized));
        ready_ = readiness.get();
    } catch (...) {
        if (window_thread_.joinable()) window_thread_.join();
        { std::lock_guard<std::mutex> lock(queue_mutex_); collector_closing_ = true; }
        queue_changed_.notify_one();
        collector_thread_.join();
        throw;
    }
}

RecordEngine::~RecordEngine() {
    if (ready_) PostThreadMessage(window_thread_id_, WM_SHUTDOWN_RECORD, 0, 0);
    if (window_thread_.joinable()) window_thread_.join();
    {
        std::lock_guard<std::mutex> lock(queue_mutex_);
        collector_closing_ = true;
    }
    queue_changed_.notify_one();
    if (collector_thread_.joinable()) collector_thread_.join();
}

bool RecordEngine::start_record(uint64_t session_id) const {
    return ready_ && session_id && PostThreadMessage(window_thread_id_, WM_START_RECORD, static_cast<WPARAM>(session_id), 0);
}

bool RecordEngine::stop_record(uint64_t session_id) const {
    return ready_ && session_id && PostThreadMessage(window_thread_id_, WM_STOP_RECORD, static_cast<WPARAM>(session_id), 0);
}
}
