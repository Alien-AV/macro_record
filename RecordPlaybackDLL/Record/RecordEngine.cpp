#include "../stdafx.h"
#include "RecordEngine.h"
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
    const auto time = static_cast<DWORD>(GetMessageTime());
    if (raw->header.dwType == RIM_TYPEKEYBOARD) pipeline_.keyboard(raw->data.keyboard, time);
    else if (raw->header.dwType == RIM_TYPEMOUSE) {
        const auto& data = raw->data.mouse;
        const auto bounds = pipeline_.session() && (data.usFlags & MOUSE_MOVE_ABSOLUTE)
            ? mouse::physical_desktop_bounds((data.usFlags & MOUSE_VIRTUAL_DESKTOP) != 0) : mouse::DesktopBounds{};
        pipeline_.mouse(data, bounds, time);
    }
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
        if (message.message == WM_FAILED_RECORD) {
            pipeline_.fail(static_cast<uint64_t>(message.wParam));
        } else if (message.message == WM_START_RECORD || message.message == WM_STOP_RECORD) {
            const auto payload = static_cast<uint64_t>(message.lParam);
            const auto gestures = static_cast<uint32_t>(payload);
            const auto cutoff = message.message == WM_STOP_RECORD && gestures
                ? static_cast<DWORD>(payload >> 32) : message.time;
            commands_.push_back({message.message, static_cast<uint64_t>(message.wParam), cutoff, gestures});
        } else {
            TranslateMessage(&message);
            DispatchMessage(&message);
        }
    }
    if (pipeline_.session()) pipeline_.stop(pipeline_.session());
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
            POINT position{};
            const bool valid = GetPhysicalCursorPos(&position) != FALSE;
            pipeline_.start(command.session, command.gestures, {position.x, position.y, valid});
        } else {
            pipeline_.stop(command.session, command.gestures, command.cutoff);
        }
    }
    // At most 256 raw messages per turn. The fixed cutoff excludes future input,
    // so a sustained device stream cannot continually extend the prefix to drain.
}

void RecordEngine::collect() {
    while (pipeline_.collect_one(true)) {}
}

RecordEngine::RecordEngine(record_events_callback_t input, status_callback_t status, boundary_callback_t boundary)
    : record_events_callback_(input), status_callback_(status), boundary_callback_(boundary),
      pipeline_([this](capture::Packet packet) {
          if (packet.event) record_events_callback_(std::move(packet.event), packet.session);
          else boundary_callback_(packet.session, packet.boundary, packet.held_keys, packet.idle_released_keys, packet.origin);
      }, [this](uint64_t session) {
          PostThreadMessage(window_thread_id_, WM_FAILED_RECORD, static_cast<WPARAM>(session), 0);
      }) {
    std::promise<bool> initialized;
    auto readiness = initialized.get_future();
    collector_thread_ = std::thread(&RecordEngine::collect, this);
    try {
        window_thread_ = std::thread(&RecordEngine::window_main, this, std::move(initialized));
        ready_ = readiness.get();
    } catch (...) {
        if (window_thread_.joinable()) window_thread_.join();
        pipeline_.close();
        collector_thread_.join();
        throw;
    }
}

RecordEngine::~RecordEngine() {
    if (ready_) PostThreadMessage(window_thread_id_, WM_SHUTDOWN_RECORD, 0, 0);
    if (window_thread_.joinable()) window_thread_.join();
    pipeline_.close();
    if (collector_thread_.joinable()) collector_thread_.join();
}

bool RecordEngine::start_record(uint64_t session_id, uint32_t stop_gestures) const {
    return ready_ && session_id && PostThreadMessage(window_thread_id_, WM_START_RECORD, static_cast<WPARAM>(session_id), stop_gestures);
}

bool RecordEngine::stop_record(uint64_t session_id, uint32_t gesture, DWORD message_time) const {
    static_assert(sizeof(LPARAM) == sizeof(uint64_t), "Recording command payload requires x64.");
    const auto payload = (static_cast<uint64_t>(message_time) << 32) | gesture;
    return ready_ && session_id && PostThreadMessage(window_thread_id_, WM_STOP_RECORD, static_cast<WPARAM>(session_id), static_cast<LPARAM>(payload));
}
}
