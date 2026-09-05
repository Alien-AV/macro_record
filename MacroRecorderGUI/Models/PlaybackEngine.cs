using System.Runtime.InteropServices;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Utils;

namespace MacroRecorderGUI.Models;

public interface IPlaybackEngine
{
    void PlaybackEvents(IEnumerable<InputEvent> events);
    void PlaybackEventAbort();
}

internal sealed class PlaybackEngine : IPlaybackEngine
{
    [DllImport("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_playback_events_abort", CallingConvention = CallingConvention.Cdecl)]
    private static extern void DllPlaybackEventAbort();

    [DllImport("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_playback_events", CallingConvention = CallingConvention.Cdecl)]
    private static extern void DllPlaybackEventsBuffer(nint buffer, int bufferSize);

    public void PlaybackEventAbort()
    {
        DllPlaybackEventAbort();
    }

    public void PlaybackEvents(IEnumerable<InputEvent> events)
    {
        PlaybackSerializedEvents(SerializeEvents.SerializeEventsToByteArray(events));
    }

    private static void PlaybackSerializedEvents(byte[] serializedEvents)
    {
        var buffer = Marshal.AllocHGlobal(serializedEvents.Length);
        try
        {
            Marshal.Copy(serializedEvents, 0, buffer, serializedEvents.Length);
            DllPlaybackEventsBuffer(buffer, serializedEvents.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
