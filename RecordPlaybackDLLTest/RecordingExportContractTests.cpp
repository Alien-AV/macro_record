#include "pch.h"
#include "../RecordPlaybackDLL/RecordPlaybackDLL.h"
#include <type_traits>

TEST(RecordingExportContract, VersionedStartIsExportedAndUnversionedStartIsAbsent) {
    using Start = bool(*)(uint64_t, uint32_t, const uint32_t*, uint32_t);
    static_assert(std::is_same<Start, decltype(&iac_dll_start_record_v2)>::value, "Capture start requires four arguments");
    std::wstring path(32768, L'\0');
    const auto length = GetModuleFileNameW(nullptr, &path[0], static_cast<DWORD>(path.size()));
    ASSERT_GT(length, 0u);
    ASSERT_LT(length, path.size());
    path.resize(length);
    const auto separator = path.find_last_of(L"\\/");
    ASSERT_NE(std::wstring::npos, separator);
    path.resize(separator + 1);
    path += L"RecordPlaybackDLL.dll";
    struct LoadedModule {
        HMODULE value;
        ~LoadedModule() { if (value) FreeLibrary(value); }
    } module{LoadLibraryExW(path.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS)};
    ASSERT_NE(nullptr, module.value) << "LoadLibraryExW error: " << GetLastError();
    // Symbol inspection only: do not initialize the recorder or call a start export.
    EXPECT_NE(nullptr, GetProcAddress(module.value, "iac_dll_start_record_v2"));
    EXPECT_EQ(nullptr, GetProcAddress(module.value, "iac_dll_start_record"));
}
