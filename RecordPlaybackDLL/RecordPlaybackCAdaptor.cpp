#include "RecordPlaybackDLL.h"
#include "Record/RecordEngine.h"
#include "Common/DeserializeEvent.h"
#include "Common/KeyboardEvent.h"
#include "Common/MouseEvent.h"
#include "Playback/PlaybackInput.h"
#include "Playback/PlaybackSession.h"
#include <limits>
#include <stdexcept>

iac_dll_status_cb_t c_callback_for_status_reporting = nullptr;
iac_dll_record_event_cb_t c_callback_for_record_event_reporting = nullptr;
std::unique_ptr<record_playback::RecordEngine> record_engine_singleton;

void convert_cpp_status_to_c_status_and_call_callback(const RecordPlaybackDLLEnums::StatusCode status_code) {
	if (c_callback_for_status_reporting) c_callback_for_status_reporting(status_code);
}

void convert_cpp_event_to_c_and_call_callback(const std::unique_ptr<Event> event) {
	if (!event || !c_callback_for_record_event_reporting) return;
	const auto bytes = event->serialize();
	if (!bytes || bytes->size() > static_cast<size_t>((std::numeric_limits<int>::max)())) {
		convert_cpp_status_to_c_status_and_call_callback(RecordPlaybackDLLEnums::ErrorCouldNotProcessInputData);
		return;
	}
	// The callback borrows this vector only for the duration of the call; managed
	// RecordEngine copies it synchronously. No unmanaged byte allocation escapes.
	c_callback_for_record_event_reporting(bytes->data(), static_cast<int>(bytes->size()));
}

RECORD_PLAYBACK_DLL_API void iac_dll_init(iac_dll_record_event_cb_t event_record_cb, iac_dll_status_cb_t status_cb) {
	c_callback_for_record_event_reporting = event_record_cb;
	c_callback_for_status_reporting = status_cb;
	record_engine_singleton = std::make_unique<record_playback::RecordEngine>(convert_cpp_event_to_c_and_call_callback, convert_cpp_status_to_c_status_and_call_callback);
}

RECORD_PLAYBACK_DLL_API void iac_dll_start_record() { record_engine_singleton->start_record(); }
RECORD_PLAYBACK_DLL_API void iac_dll_stop_record() { record_engine_singleton->stop_record(); }

namespace {
record_playback::PlaybackSession playback_session([](const Event& event) {
	if (const auto keyboard = dynamic_cast<const KeyboardEvent*>(&event))
		return WindowsInjectionAPI::playback_keyboard_event(keyboard->virtualKeyCode, keyboard->keyUp);
	if (const auto mouse = dynamic_cast<const MouseEvent*>(&event))
		return WindowsInjectionAPI::playback_mouse_event(mouse->x, mouse->y, mouse->wheelRotation, mouse->relative_position, mouse->ActionType, mouse->mappedToVirtualDesktop);
	return false;
}, true);
}

RECORD_PLAYBACK_DLL_API PlaybackResult iac_dll_playback_start(const unsigned char buffer[], size_t size, int loop, uint64_t* session_id) noexcept {
	if (!session_id) return PlaybackResult::InvalidInput;
	*session_id = 0;
	if (!buffer || !size || size > static_cast<size_t>((std::numeric_limits<int>::max)()) || (loop != 0 && loop != 1))
		return PlaybackResult::InvalidInput;
	try {
		return playback_session.start(record_playback::deserialize_events({buffer, buffer + size}), loop != 0, *session_id);
	} catch (const std::invalid_argument&) { return PlaybackResult::InvalidInput; }
	catch (...) { return PlaybackResult::InternalError; }
}

RECORD_PLAYBACK_DLL_API PlaybackResult iac_dll_playback_poll(uint64_t session_id) noexcept {
	try { return playback_session.poll(session_id); }
	catch (...) { return PlaybackResult::InternalError; }
}

RECORD_PLAYBACK_DLL_API PlaybackResult iac_dll_playback_abort(uint64_t session_id) noexcept {
	try { return playback_session.abort(session_id); }
	catch (...) { return PlaybackResult::InternalError; }
}

RECORD_PLAYBACK_DLL_API PlaybackResult iac_dll_playback_set_loop(uint64_t session_id, int loop) noexcept {
	if (loop != 0 && loop != 1) return PlaybackResult::InvalidInput;
	try { return playback_session.set_loop(session_id, loop != 0); }
	catch (...) { return PlaybackResult::InternalError; }
}

RECORD_PLAYBACK_DLL_API void iac_dll_playback_shutdown() noexcept {
	try { playback_session.shutdown(); }
	catch (...) { /* No exceptions may cross the C ABI during shutdown. */ }
}
