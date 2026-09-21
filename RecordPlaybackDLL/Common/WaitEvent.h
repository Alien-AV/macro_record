#pragma once
#include "Event.h"

class WaitEvent final : public Event {
public:
    protobufGenerated::WaitCondition condition;
    static bool valid(const protobufGenerated::WaitCondition& wait);
    std::unique_ptr<std::vector<unsigned char>> serialize() const override;
    void playback() const override;
};
