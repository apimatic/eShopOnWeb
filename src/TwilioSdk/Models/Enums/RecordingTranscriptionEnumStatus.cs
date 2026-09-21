using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status of the transcription. Can be: <c>in-progress</c>, <c>completed</c>, <c>failed</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RecordingTranscriptionEnumStatus>))]
public sealed record RecordingTranscriptionEnumStatus : StringEnum<RecordingTranscriptionEnumStatus>
{
    private RecordingTranscriptionEnumStatus(string value) : base(value)
    {
    }

    public static readonly RecordingTranscriptionEnumStatus InProgress = new("in-progress");

    public static readonly RecordingTranscriptionEnumStatus Completed = new("completed");

    public static readonly RecordingTranscriptionEnumStatus Failed = new("failed");

    public static RecordingTranscriptionEnumStatus FromValue(string value) => FromValueCore(value);
}
