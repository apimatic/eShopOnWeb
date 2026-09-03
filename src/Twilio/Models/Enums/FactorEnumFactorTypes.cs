using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The Type of this Factor. Currently <c>push</c> and <c>totp</c> are supported.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<FactorEnumFactorTypes>))]
public sealed record FactorEnumFactorTypes : StringEnum<FactorEnumFactorTypes>
{
    private FactorEnumFactorTypes(string value) : base(value)
    {
    }

    public static readonly FactorEnumFactorTypes Push = new("push");

    public static readonly FactorEnumFactorTypes Totp = new("totp");

    public static readonly FactorEnumFactorTypes Passkeys = new("passkeys");

    public static FactorEnumFactorTypes FromValue(string value) => FromValueCore(value);
}
