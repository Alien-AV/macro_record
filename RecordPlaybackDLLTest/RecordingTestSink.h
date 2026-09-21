#pragma once
#include "../RecordPlaybackDLL/Record/RecordingDelivery.h"

// State/filter unit tests consume deliveries immediately. Pipeline tests instead
// schedule the real collector independently from their raw source and clock.
inline record_playback::capture::Stream::Sink collect_packets(std::vector<record_playback::capture::Packet>& received) {
    auto collector = std::make_shared<record_playback::capture::DeliveryCollector>(
        [&received](record_playback::capture::Packet packet) { received.push_back(std::move(packet)); });
    return [collector](record_playback::capture::Packet packet) { collector->input(std::move(packet)); };
}
inline record_playback::capture::StopChord::Sink collect_events(
    std::function<void(std::unique_ptr<Event>)> sink) {
    return [sink](record_playback::capture::CapturedInput input) {
        if (input.pending) input.pending->drain(input.omit_command, sink);
        else sink(std::move(input.event));
    };
}
