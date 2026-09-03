using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The Type of the Factor. Currently only <c>push</c> is supported.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<AccessTokenEnumFactorTypes>))]
public sealed record AccessTokenEnumFactorTypes : StringEnum<AccessTokenEnumFactorTypes>
{
    private AccessTokenEnumFactorTypes(string value) : base(value)
    {
    }

    public static readonly AccessTokenEnumFactorTypes Push = new("push");

    public static AccessTokenEnumFactorTypes FromValue(string value) => FromValueCore(value);
}
