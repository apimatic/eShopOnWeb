using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The type of the Transfer. Can be: <c>cold</c>, <c>warm</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<InteractionTransferEnumTransferType>))]
public sealed record InteractionTransferEnumTransferType : StringEnum<InteractionTransferEnumTransferType>
{
    private InteractionTransferEnumTransferType(string value) : base(value)
    {
    }

    public static readonly InteractionTransferEnumTransferType Warm = new("warm");

    public static readonly InteractionTransferEnumTransferType Cold = new("cold");

    public static readonly InteractionTransferEnumTransferType External = new("external");

    public static InteractionTransferEnumTransferType FromValue(string value) => FromValueCore(value);
}
