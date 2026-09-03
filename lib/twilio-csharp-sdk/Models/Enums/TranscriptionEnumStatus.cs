using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The status of the transcription. Can be: <c>in-progress</c>, <c>completed</c>, <c>failed</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<TranscriptionEnumStatus>))]
public sealed record TranscriptionEnumStatus : StringEnum<TranscriptionEnumStatus>
{
    private TranscriptionEnumStatus(string value) : base(value)
    {
    }

    public static readonly TranscriptionEnumStatus InProgress = new("in-progress");

    public static readonly TranscriptionEnumStatus Completed = new("completed");

    public static readonly TranscriptionEnumStatus Failed = new("failed");

    public static TranscriptionEnumStatus FromValue(string value) => FromValueCore(value);
}
