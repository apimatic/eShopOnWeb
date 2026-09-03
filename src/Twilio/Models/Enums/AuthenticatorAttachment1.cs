using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<AuthenticatorAttachment1>))]
public sealed record AuthenticatorAttachment1 : StringEnum<AuthenticatorAttachment1>
{
    private AuthenticatorAttachment1(string value) : base(value)
    {
    }

    public static readonly AuthenticatorAttachment1 Platform = new("platform");

    public static readonly AuthenticatorAttachment1 CrossPlatform = new("cross-platform");

    public static readonly AuthenticatorAttachment1 Any = new("any");

    public static AuthenticatorAttachment1 FromValue(string value) => FromValueCore(value);
}
