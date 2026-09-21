using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// A string that indicates the mechanism by which the WebAuthn implementation is attached to the authenticator at the time the associated <c>navigator.credentials.create()</c> or <c>navigator.credentials.get()</c> call completes.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<AuthenticatorAttachment2>))]
public sealed record AuthenticatorAttachment2 : StringEnum<AuthenticatorAttachment2>
{
    private AuthenticatorAttachment2(string value) : base(value)
    {
    }

    public static readonly AuthenticatorAttachment2 Platform = new("platform");

    public static readonly AuthenticatorAttachment2 CrossPlatform = new("cross-platform");

    public static AuthenticatorAttachment2 FromValue(string value) => FromValueCore(value);
}
