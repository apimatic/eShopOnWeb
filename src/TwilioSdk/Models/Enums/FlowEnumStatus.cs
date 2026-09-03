using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status of the Flow. Can be: <c>draft</c> or <c>published</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<FlowEnumStatus>))]
public sealed record FlowEnumStatus : StringEnum<FlowEnumStatus>
{
    private FlowEnumStatus(string value) : base(value)
    {
    }

    public static readonly FlowEnumStatus Draft = new("draft");

    public static readonly FlowEnumStatus Published = new("published");

    public static FlowEnumStatus FromValue(string value) => FromValueCore(value);
}
