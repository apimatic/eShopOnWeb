using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// A string that indicates the mechanism by which the WebAuthn implementation is attached to the authenticator at the time the associated
/// <c>navigator.credentials.create()</c> or <c>navigator.credentials.get()</c> call completes.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<AuthenticatorAttachment>))]
public sealed record AuthenticatorAttachment : StringEnum<AuthenticatorAttachment>
{
    private AuthenticatorAttachment(string value) : base(value)
    {
    }

    public static readonly AuthenticatorAttachment Platform = new("platform");

    public static readonly AuthenticatorAttachment CrossPlatform = new("cross-platform");

    public static AuthenticatorAttachment FromValue(string value) => FromValueCore(value);
}
