#pragma once
#include "../RecordPlaybackDLL.h"
#include "../Common/Event.h"
#include <atomic>
#include <condition_variable>
#include <functional>
#include <mutex>
#include <thread>

namespace record_playback {
// One worker owns injection and cleanup. Sinks must return promptly and must not
// reenter this session. Tests supply a sink that never calls SendInput.
class PlaybackSession {
public:
	using Sink = std::function<bool(const Event&)>;
	RECORD_PLAYBACK_DLL_API explicit PlaybackSession(Sink sink, bool release_start_modifiers = false);
	RECORD_PLAYBACK_DLL_API ~PlaybackSession();
	PlaybackSession(const PlaybackSession&) = delete;
	PlaybackSession& operator=(const PlaybackSession&) = delete;
	RECORD_PLAYBACK_DLL_API PlaybackResult start(std::vector<std::unique_ptr<Event>> events, bool loop, uint64_t& id);
	RECORD_PLAYBACK_DLL_API PlaybackResult poll(uint64_t id);
	RECORD_PLAYBACK_DLL_API PlaybackResult wait_request(uint64_t id, PlaybackWaitRequest& request);
	RECORD_PLAYBACK_DLL_API PlaybackResult resolve_wait(uint64_t id, uint64_t occurrence, bool satisfied);
	RECORD_PLAYBACK_DLL_API PlaybackResult set_loop(uint64_t id, bool loop);
	RECORD_PLAYBACK_DLL_API PlaybackResult abort(uint64_t id);
	RECORD_PLAYBACK_DLL_API void shutdown();
private:
	void run(std::vector<std::unique_ptr<Event>> events) noexcept;
	bool wait_until(std::chrono::steady_clock::time_point deadline);
	void stop_and_join();
	Sink sink_;
	bool release_start_modifiers_;
	std::mutex lifecycle_mutex_;
	std::mutex wait_mutex_;
	std::condition_variable wake_;
	std::thread worker_;
	std::atomic<bool> cancelled_{false};
	std::atomic<bool> loop_{false};
	std::atomic<PlaybackResult> result_{PlaybackResult::Finished};
	uint64_t generation_ = 0;
	uint64_t occurrence_ = 0;
	PlaybackWaitRequest waiting_{};
	std::chrono::steady_clock::time_point wait_deadline_{};
	int resolution_ = 0;
	bool loop_safe_ = true;
};
}
