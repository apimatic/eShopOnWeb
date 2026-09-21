using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Status4>))]
public sealed record Status4 : StringEnum<Status4>
{
    private Status4(string value) : base(value)
    {
    }

    public static readonly Status4 Unknown = new("unknown");

    public static readonly Status4 CreationInProgress = new("creation-in-progress");

    public static readonly Status4 Ready = new("ready");

    public static readonly Status4 CreationFailed = new("creation-failed");

    public static readonly Status4 DeletionInProgress = new("deletion-in-progress");

    public static readonly Status4 Deleted = new("deleted");

    public static readonly Status4 DeletionFailed = new("deletion-failed");

    public static Status4 FromValue(string value) => FromValueCore(value);
}
