using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The Type of this Factor. Currently <c>push</c> and <c>totp</c> are supported.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<NewFactorEnumFactorTypes>))]
public sealed record NewFactorEnumFactorTypes : StringEnum<NewFactorEnumFactorTypes>
{
    private NewFactorEnumFactorTypes(string value) : base(value)
    {
    }

    public static readonly NewFactorEnumFactorTypes Push = new("push");

    public static readonly NewFactorEnumFactorTypes Totp = new("totp");

    public static readonly NewFactorEnumFactorTypes Passkeys = new("passkeys");

    public static NewFactorEnumFactorTypes FromValue(string value) => FromValueCore(value);
}
