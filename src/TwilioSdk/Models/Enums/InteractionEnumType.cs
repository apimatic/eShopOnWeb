using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The Type of the Interaction. Can be: <c>message</c>, <c>voice</c> or <c>unknown</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<InteractionEnumType>))]
public sealed record InteractionEnumType : StringEnum<InteractionEnumType>
{
    private InteractionEnumType(string value) : base(value)
    {
    }

    public static readonly InteractionEnumType Message = new("message");

    public static readonly InteractionEnumType Voice = new("voice");

    public static readonly InteractionEnumType Unknown = new("unknown");

    public static InteractionEnumType FromValue(string value) => FromValueCore(value);
}
