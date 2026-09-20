using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Protobuf;
using MacroRecorderGUI.Models;
using ProtobufGenerated;

namespace MacroRecorderGUI.Utils;

/// <summary>A fail-closed envelope; legacy protobuf bytes retain their original meaning.</summary>
internal sealed record RecordingDocument
{
    // Zero is an invalid protobuf tag: old readers cannot silently ignore origin metadata.
    private static ReadOnlySpan<byte> Magic => "\0MACRO2\n"u8;
    public int Version { get; init; } = 2;
    public required byte[] Events { get; init; }
    public PointerOriginBoundary[] Origins { get; init; } = [];
    public byte[]? BeforeOriginAdoption { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extensions { get; init; }
    [JsonIgnore] public bool IsExtended { get; init; }

    public static RecordingDocument Read(byte[] bytes) => Read(bytes, 0);

    private static RecordingDocument Read(byte[] bytes, int recoveryDepth)
    {
        if (recoveryDepth > 4) throw new InvalidDataException("Recording recovery nesting exceeds the supported limit.");
        if (bytes.Length > RecordingLibraryStore.MaximumMacroBytes) throw new InvalidDataException("Macros must be 64 MB or smaller.");
        if (!bytes.AsSpan().StartsWith(Magic))
        {
            _ = ProtobufInputEventList.Parser.ParseFrom(bytes);
            return new() { Events = bytes.ToArray() };
        }
        var document = JsonSerializer.Deserialize<RecordingDocument>(bytes.AsSpan(Magic.Length))
            ?? throw new InvalidDataException("Missing recording document.");
        if (document.Version != 2 || document.Events is null || document.Origins is null)
            throw new InvalidDataException("This recording requires a different file-format version.");
        var count = ProtobufInputEventList.Parser.ParseFrom(document.Events).InputEvents.Count;
        var previous = -1;
        foreach (var origin in document.Origins)
        {
            if (origin is null || origin.EventIndex < previous || origin.EventIndex < 0 || origin.EventIndex > count)
                throw new InvalidDataException("Invalid pointer origin boundary.");
            previous = origin.EventIndex;
            if (origin.AdoptedEvent is not null && origin.Position is not null) _ = origin.SetupEvent();
        }
        if (document.BeforeOriginAdoption is { } recovery)
            _ = Read(recovery, recoveryDepth + 1);
        return document with { IsExtended = true };
    }

    public byte[] Write()
    {
        if (!IsExtended && Origins.Length == 0 && BeforeOriginAdoption is null) return Events.ToArray();
        var json = JsonSerializer.SerializeToUtf8Bytes(this);
        var bytes = new byte[Magic.Length + json.Length];
        Magic.CopyTo(bytes); json.CopyTo(bytes, Magic.Length);
        if (bytes.Length > RecordingLibraryStore.MaximumMacroBytes) throw new InvalidDataException("Macros must be 64 MB or smaller.");
        return bytes;
    }

    public ProtobufInputEventList ParseEvents() => ProtobufInputEventList.Parser.ParseFrom(Events);

    public byte[] ExportLegacy()
    {
        var wire = ParseEvents();
        PointerPlayback.Validate(Origins, wire.InputEvents.Count, PlaybackPointerOrigin.RecordedStartingPoint);
        // Use stored physical positions and unscaled delays; do not sample or inject input.
        var raw = wire.InputEvents.Select(input => input.Clone()).ToArray();
        wire.InputEvents.Clear();
        var boundary = 0;
        for (var index = 0; index <= raw.Length; index++)
        {
            while (boundary < Origins.Length && Origins[boundary].EventIndex == index)
            {
                var origin = Origins[boundary++];
                wire.InputEvents.Add(origin.SetupEvent());
            }
            if (index < raw.Length) wire.InputEvents.Add(raw[index]);
        }
        return Origins.Length == 0 ? Events.ToArray() : wire.ToByteArray();
    }
}
