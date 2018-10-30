#if __cplusplus
#pragma once
#define public
#endif

namespace RecordPlaybackDLLEnums
{
    public enum StatusCode
    {
        PlaybackFinished,
        ErrorCouldNotProcessInputData,
    };
}

#if __cplusplus
#undef public
#endif