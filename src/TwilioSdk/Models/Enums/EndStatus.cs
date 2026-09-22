using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// End status of the call wrap up event.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<EndStatus>))]
public sealed record EndStatus : StringEnum<EndStatus>
{
    private EndStatus(string value) : base(value)
    {
    }

    public static readonly EndStatus Unknown = new("unknown");

    public static readonly EndStatus Failure = new("failure");

    public static readonly EndStatus Ended = new("ended");

    public static readonly EndStatus HungUp = new("hung_up");

    public static EndStatus FromValue(string value) => FromValueCore(value);
}
