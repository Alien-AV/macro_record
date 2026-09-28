#pragma once
#include "Event.h"
#include <stdexcept>

class DelayEvent final : public Event {
public:
    std::chrono::microseconds duration{};
    static bool valid(uint64_t value) { return value <= 86400000000ULL; }
    std::unique_ptr<std::vector<unsigned char>> serialize() const override {
        auto wire = std::make_unique<protobufGenerated::ProtobufInputEvent>();
        wire->set_timesincelastevent(time_since_last_event.count());
        wire->mutable_delay()->set_duration_microseconds(duration.count());
        return input_event_to_uchar_vector(wire);
    }
    void playback() const override { throw std::logic_error("Fixed delays require a playback session"); }
};
