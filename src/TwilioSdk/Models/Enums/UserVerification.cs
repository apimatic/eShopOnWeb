using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<UserVerification>))]
public sealed record UserVerification : StringEnum<UserVerification>
{
    private UserVerification(string value) : base(value)
    {
    }

    public static readonly UserVerification Required = new("required");

    public static readonly UserVerification Preferred = new("preferred");

    public static readonly UserVerification Discouraged = new("discouraged");

    public static UserVerification FromValue(string value) => FromValueCore(value);
}
