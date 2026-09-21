#include "WaitEvent.h"
#include <stdexcept>
#include <algorithm>
#include <cctype>

namespace {
bool selector(const protobufGenerated::WindowSelector& value) {
    const std::string* fields[] = { &value.executable_path(), &value.window_class(), &value.title() };
    bool nonempty = false;
    for (const auto field : fields) {
        if (field->size() > 4096 || field->find('\0') != std::string::npos) return false;
        nonempty |= std::any_of(field->begin(), field->end(), [](unsigned char c) { return !std::isspace(c); });
    }
    const auto& path = value.executable_path();
    if (!path.empty() && !(path.size() >= 3 && std::isalpha(static_cast<unsigned char>(path[0])) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
        && !(path.size() >= 3 && path[0] == '\\' && path[1] == '\\')) return false;
    return nonempty && protobufGenerated::TitleMatch_IsValid(value.title_match());
}
}

bool WaitEvent::valid(const protobufGenerated::WaitCondition& wait) {
    using namespace protobufGenerated;
    if (wait.semantics_version() != 1 || !WaitTrigger_IsValid(wait.trigger())
        || wait.timeout_us() < 1000 || wait.timeout_us() > 86400000000ULL
        || wait.stable_for_us() > wait.timeout_us() || wait.poll_interval_us() < 10000 || wait.poll_interval_us() > 1000000) return false;
    if (wait.has_window()) {
        const auto& window = wait.window();
        return window.has_target() && selector(window.target()) && WindowTest_IsValid(window.test())
            && (wait.trigger() != NEW_WINDOW || window.test() == EXISTS || window.test() == VISIBLE)
            && (wait.trigger() != CHANGES || (!window.target().any_match() && window.test() != ABSENT));
    }
    if (wait.has_pixel()) {
        const auto& pixel = wait.pixel();
        if (!PixelCoordinates_IsValid(pixel.coordinates()) || pixel.rgb() > 0xffffff || pixel.tolerance() > 255
            || wait.trigger() == NEW_WINDOW || pixel.reference_width() > 1000000 || pixel.reference_height() > 1000000) return false;
        if (pixel.coordinates() == DESKTOP_PHYSICAL) return !pixel.has_target();
        return pixel.has_target() && selector(pixel.target()) && !pixel.target().any_match() && pixel.x() >= 0 && pixel.y() >= 0
            && (pixel.coordinates() != CLIENT_LOGICAL || (pixel.reference_dpi() >= 48 && pixel.reference_dpi() <= 768));
    }
    return false;
}

std::unique_ptr<std::vector<unsigned char>> WaitEvent::serialize() const {
    auto wire = std::make_unique<protobufGenerated::ProtobufInputEvent>();
    wire->set_timesincelastevent(time_since_last_event.count());
    *wire->mutable_waitcondition() = condition;
    return input_event_to_uchar_vector(wire);
}
void WaitEvent::playback() const { throw std::logic_error("Conditional waits require a playback session"); }
