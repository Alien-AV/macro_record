#pragma once
#include <algorithm>
#include <array>
#include <functional>
#include <limits>
#include <stdexcept>
#include <string>
#include "../Common/KeyboardEvent.h"
#include "../Common/MouseEvent.h"

namespace record_playback { namespace capture {
class PendingInputError : public std::runtime_error {
public:
    PendingInputError() : std::runtime_error("Could not retain provisional recording input") {}
};

// Private capture storage, never a macro file. One delete-on-close handle and a
// fixed RAM batch retain exact samples until command provenance is available.
class PendingStorage {
public:
    virtual ~PendingStorage() = default;
    virtual void write(const void* data, DWORD bytes) = 0;
    virtual void rewind() = 0;
    virtual void read(void* data, DWORD bytes) = 0;
};

class TemporaryPendingStorage final : public PendingStorage {
public:
    TemporaryPendingStorage() {
        wchar_t directory[MAX_PATH + 1]{};
        wchar_t path[MAX_PATH + 1]{};
        const auto length = GetTempPathW(MAX_PATH + 1, directory);
        if (!length || length > MAX_PATH || !GetTempFileNameW(directory, L"mrc", 0, path))
            throw PendingInputError();
        // Allocate the diagnostic path before opening the handle. No raw data
        // exists until delete-on-close has been established successfully.
        try { path_ = path; }
        catch (...) { DeleteFileW(path); throw; }
        file_ = CreateFileW(path, GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING,
            FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_DELETE_ON_CLOSE | FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
        if (file_ == INVALID_HANDLE_VALUE) {
            DeleteFileW(path);
            throw PendingInputError();
        }
    }
    ~TemporaryPendingStorage() override { CloseHandle(file_); }
    TemporaryPendingStorage(const TemporaryPendingStorage&) = delete;
    TemporaryPendingStorage& operator=(const TemporaryPendingStorage&) = delete;
    const std::wstring& path() const { return path_; }
    void write(const void* data, DWORD bytes) override {
        DWORD written = 0;
        if (!WriteFile(file_, data, bytes, &written, nullptr) || written != bytes) throw PendingInputError();
    }
    void rewind() override {
        LARGE_INTEGER zero{};
        if (!SetFilePointerEx(file_, zero, nullptr, FILE_BEGIN)) throw PendingInputError();
    }
    void read(void* data, DWORD bytes) override {
        DWORD received = 0;
        if (!ReadFile(file_, data, bytes, &received, nullptr) || received != bytes) throw PendingInputError();
    }
private:
    HANDLE file_ = INVALID_HANDLE_VALUE;
    std::wstring path_;
};

class PendingInput {
    struct Entry { std::unique_ptr<Event> event; bool command_key = false; };
    struct Stored {
        int64_t delay = 0;
        LONG x = 0, y = 0;
        DWORD flags = 0, data = 0;
        WORD key = 0, scan = 0;
        bool keyboard = false, up = false, relative = false, virtual_desktop = false, command_key = false;
    };
public:
    using Factory = std::function<std::unique_ptr<PendingStorage>()>;
    static constexpr size_t capacity = 256;
    // Exhaustion fails the capture explicitly; it never publishes command keys
    // or silently drops motion to make room. At 8 kHz this holds minutes of input.
    static constexpr size_t max_bytes = 64 * 1024 * 1024;
    explicit PendingInput(Factory factory = [] { return std::make_unique<TemporaryPendingStorage>(); },
        size_t max_events = max_bytes / sizeof(Stored)) : factory_(std::move(factory)), max_events_(max_events) {}
    size_t memory_count() const { return memory_count_; }
    bool empty() const { return !memory_count_ && !stored_count_; }
    void append(std::unique_ptr<Event> event, bool command_key) {
        if (stored_count_ + memory_count_ >= max_events_) throw PendingInputError();
        if (memory_count_ == capacity) spill();
        memory_[memory_count_++] = {std::move(event), command_key};
    }
    template<class Sink> void drain(bool omit_command, Sink sink) {
        std::chrono::microseconds omitted{};
        const auto emit = [&](std::unique_ptr<Event> event, bool command_key) {
            const auto delay = event->time_since_last_event;
            if (delay.count() < 0 || delay.count() > (std::numeric_limits<int64_t>::max)() - omitted.count())
                throw PendingInputError();
            if (omit_command && command_key) omitted += delay;
            else {
                event->time_since_last_event += omitted;
                omitted = {};
                sink(std::move(event));
            }
        };
        if (storage_) {
            storage_->rewind();
            std::array<Stored, capacity> batch{};
            for (size_t offset = 0; offset < stored_count_;) {
                const auto count = (std::min)(capacity, stored_count_ - offset);
                storage_->read(batch.data(), static_cast<DWORD>(count * sizeof(Stored)));
                for (size_t i = 0; i < count; ++i) emit(restore(batch[i]), batch[i].command_key);
                offset += count;
            }
        }
        for (size_t i = 0; i < memory_count_; ++i) emit(std::move(memory_[i].event), memory_[i].command_key);
        clear();
    }
    void clear() {
        for (size_t i = 0; i < memory_count_; ++i) memory_[i].event.reset();
        memory_count_ = stored_count_ = 0;
        storage_.reset();
    }
private:
    static Stored snapshot(const Entry& entry) {
        Stored stored{};
        stored.delay = entry.event->time_since_last_event.count();
        stored.command_key = entry.command_key;
        if (const auto key = dynamic_cast<const KeyboardEvent*>(entry.event.get())) {
            stored.keyboard = true;
            stored.key = key->virtualKeyCode;
            stored.scan = key->hardwareScanCode;
            stored.up = key->keyUp;
        } else if (const auto mouse = dynamic_cast<const MouseEvent*>(entry.event.get())) {
            stored.x = mouse->x; stored.y = mouse->y;
            stored.flags = mouse->ActionType; stored.data = mouse->wheelRotation;
            stored.relative = mouse->relative_position; stored.virtual_desktop = mouse->mappedToVirtualDesktop;
        } else throw PendingInputError();
        return stored;
    }
    static std::unique_ptr<Event> restore(const Stored& stored) {
        std::unique_ptr<Event> event;
        if (stored.keyboard) {
            auto key = std::make_unique<KeyboardEvent>();
            key->virtualKeyCode = stored.key; key->hardwareScanCode = stored.scan; key->keyUp = stored.up;
            event = std::move(key);
        } else event = std::make_unique<MouseEvent>(stored.x, stored.y, stored.flags, stored.data,
            stored.virtual_desktop, stored.relative);
        event->time_since_last_event = std::chrono::microseconds(stored.delay);
        return event;
    }
    void spill() {
        if (!storage_) storage_ = factory_();
        if (!storage_) throw PendingInputError();
        std::array<Stored, capacity> batch{};
        for (size_t i = 0; i < memory_count_; ++i) batch[i] = snapshot(memory_[i]);
        storage_->write(batch.data(), static_cast<DWORD>(memory_count_ * sizeof(Stored)));
        stored_count_ += memory_count_;
        for (auto& entry : memory_) entry.event.reset();
        memory_count_ = 0;
    }
    Factory factory_;
    size_t max_events_;
    std::unique_ptr<PendingStorage> storage_;
    std::array<Entry, capacity> memory_{};
    size_t memory_count_ = 0, stored_count_ = 0;
};
}}
