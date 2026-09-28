namespace MacroRecorderGUI.Models;

/// <summary>Win32 modifier bits (without MOD_NOREPEAT) and a non-modifier virtual key.</summary>
public readonly record struct RecordingCaptureGesture(uint Modifiers, uint VirtualKey);

public enum CapturedWaitSubmission
{
    /// <summary>Submission was enqueued, not inserted. Accepted waits arrive through ordered recording input callbacks.</summary>
    Queued,
    Inactive, Stale, Unregistered, EnqueueFailed
}
