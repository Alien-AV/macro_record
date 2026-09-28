#include "WaitEvent.h"
#include <stdexcept>
#include <algorithm>
#include <cctype>
#include <cmath>
#include <locale.h>
#include <limits>

namespace {
bool text_bound(const std::string& value, int maximum) {
    if (value.find('\0') != std::string::npos || value.size() > static_cast<size_t>(maximum) * 4) return false;
    if (value.empty()) return true;
    const auto count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0);
    return count > 0 && count <= maximum;
}
bool full_path(const std::string& path) {
    return text_bound(path, 1024) && ((path.size() >= 3 && std::isalpha(static_cast<unsigned char>(path[0])) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
        || (path.size() >= 3 && path[0] == '\\' && path[1] == '\\'));
}
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
bool single_selector(const protobufGenerated::WindowSelector& value) { return !value.any_match() && selector(value); }
bool accessibility_selector(const protobufGenerated::AccessibilitySelector& value) {
    return text_bound(value.automation_id(), 1024) && (!value.automation_id().empty() || value.control_type())
        && (!value.control_type() || (value.control_type() >= 50000 && value.control_type() <= 50040));
}
bool predicate(const protobufGenerated::TextPredicate& value) {
    return text_bound(value.expected(), 32768) && protobufGenerated::TextComparison_IsValid(value.comparison())
        && protobufGenerated::TextWhitespace_IsValid(value.whitespace());
}
bool region(const protobufGenerated::OcrRegion& value) {
    using namespace protobufGenerated;
    if (!PixelCoordinates_IsValid(value.coordinates()) || !value.width() || !value.height() || value.width() > 4096 || value.height() > 4096
        || static_cast<uint64_t>(value.width()) * value.height() > 1000000 || static_cast<int64_t>(value.x()) + value.width() > INT_MAX
        || static_cast<int64_t>(value.y()) + value.height() > INT_MAX) return false;
    if (value.coordinates() == DESKTOP_PHYSICAL) return !value.has_target();
    return value.has_target() && single_selector(value.target()) && value.x() >= 0 && value.y() >= 0
        && (value.coordinates() != CLIENT_LOGICAL || (value.reference_dpi() >= 48 && value.reference_dpi() <= 768));
}
bool scalar(const protobufGenerated::MemoryCondition& memory) {
    using namespace protobufGenerated;
    const auto& value = memory.expected();
    if (!MemoryScalarType_IsValid(memory.scalar_type()) || !NumericComparison_IsValid(memory.comparison()) || value.empty() || value.size() > 128
        || std::any_of(value.begin(), value.end(), [](unsigned char c) { return std::isspace(c) || c == 0; })
        || !std::isfinite(memory.tolerance()) || memory.tolerance() < 0) return false;
    const bool floating = memory.scalar_type() == FLOAT32 || memory.scalar_type() == FLOAT64;
    if (memory.tolerance() != 0 && (!floating || (memory.comparison() != NUMERIC_EQUALS && memory.comparison() != NUMERIC_NOT_EQUALS))) return false;
    const char* begin = value.data(); const auto end = begin + value.size();
    if (*begin == '+') {
        ++begin;
        if (begin != end && (*begin == '+' || *begin == '-')) return false;
    }
    if (begin == end) return false;
    if (floating) {
        if (!std::all_of(begin, end, [](char c) { return (c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E'; })) return false;
        const auto locale = _create_locale(LC_NUMERIC, "C"); if (!locale) return false;
        char* stopped = nullptr; const auto parsed = _strtod_l(begin, &stopped, locale); _free_locale(locale);
        return stopped == end && stopped != begin && std::isfinite(parsed)
            && (memory.scalar_type() != FLOAT32 || std::isfinite(static_cast<float>(parsed)));
    }
    const bool sign = memory.scalar_type() == protobufGenerated::INT8 || memory.scalar_type() == protobufGenerated::INT16
        || memory.scalar_type() == protobufGenerated::INT32 || memory.scalar_type() == protobufGenerated::INT64;
    const auto width = memory.scalar_type() <= protobufGenerated::INT8 ? 8 : memory.scalar_type() <= protobufGenerated::INT16 ? 16
        : memory.scalar_type() <= protobufGenerated::INT32 ? 32 : 64;
    const bool negative = *begin == '-'; if (negative) ++begin;
    if (begin == end) return false;
    const auto limit = sign ? (uint64_t{1} << (width - 1)) - (negative ? 0 : 1)
        : width == 64 ? (std::numeric_limits<uint64_t>::max)() : (uint64_t{1} << width) - 1;
    uint64_t parsed = 0;
    for (; begin != end; ++begin) {
        if (*begin < '0' || *begin > '9') return false;
        const auto digit = static_cast<uint64_t>(*begin - '0');
        if (digit > limit || parsed > (limit - digit) / 10) return false;
        parsed = parsed * 10 + digit;
    }
    return sign || !negative || parsed == 0;
}
}

bool WaitEvent::valid(const protobufGenerated::WaitCondition& wait) {
    using namespace protobufGenerated;
    const bool classic = wait.has_window() || wait.has_pixel();
    if (wait.semantics_version() != (classic ? 1 : 2) || !WaitTrigger_IsValid(wait.trigger())
        || wait.timeout_us() < 1000 || wait.timeout_us() > 86400000000ULL
        || wait.stable_for_us() > wait.timeout_us() || wait.poll_interval_us() < 10000 || wait.poll_interval_us() > 1000000) return false;
    if (wait.has_window()) {
        const auto& window = wait.window();
        return window.has_target() && selector(window.target()) && WindowTest_IsValid(window.test())
            && (wait.trigger() != NEW_WINDOW || window.test() == EXISTS || window.test() == VISIBLE)
            && (wait.trigger() != CHANGES || (!window.target().any_match() && (window.test() == VISIBLE || window.test() == FOREGROUND)));
    }
    if (wait.has_pixel()) {
        const auto& pixel = wait.pixel();
        if (!PixelCoordinates_IsValid(pixel.coordinates()) || pixel.rgb() > 0xffffff || pixel.tolerance() > 255
            || wait.trigger() == NEW_WINDOW || pixel.reference_width() > 1000000 || pixel.reference_height() > 1000000) return false;
        if (pixel.coordinates() == DESKTOP_PHYSICAL) return !pixel.has_target();
        return pixel.has_target() && selector(pixel.target()) && !pixel.target().any_match() && pixel.x() >= 0 && pixel.y() >= 0
            && (pixel.coordinates() != CLIENT_LOGICAL || (pixel.reference_dpi() >= 48 && pixel.reference_dpi() <= 768));
    }
    if (wait.trigger() == NEW_WINDOW) return false;
    if (wait.has_accessibility_text()) {
        const auto& text = wait.accessibility_text();
        if (wait.poll_interval_us() < 250000 || !text.has_target() || !single_selector(text.target()) || !text.has_element() || !accessibility_selector(text.element())
            || text.ancestors_size() > 16 || !AccessibilityTextSource_IsValid(text.source()) || !text.has_predicate() || !predicate(text.predicate())) return false;
        return std::all_of(text.ancestors().begin(), text.ancestors().end(), accessibility_selector);
    }
    if (wait.has_ocr_text()) {
        const auto& ocr = wait.ocr_text(); const auto& language = ocr.language();
        return wait.poll_interval_us() >= 500000 && ocr.has_region() && region(ocr.region()) && !language.empty() && language.size() <= 64
            && std::all_of(language.begin(), language.end(), [](unsigned char c) { return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_'; })
            && ocr.has_predicate() && predicate(ocr.predicate());
    }
    if (wait.has_memory()) {
        const auto& memory = wait.memory();
        if (wait.poll_interval_us() < 100000 || !full_path(memory.executable_path()) || memory.pointer_offsets_size() > 16 || !scalar(memory)) return false;
        if (memory.has_module()) {
            const auto& module = memory.module();
            return full_path(module.path()) && text_bound(module.file_version(), 128)
                && std::any_of(module.file_version().begin(), module.file_version().end(), [](unsigned char c) { return !std::isspace(c); });
        }
        return memory.has_absolute_address() && memory.absolute_address() != 0;
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
