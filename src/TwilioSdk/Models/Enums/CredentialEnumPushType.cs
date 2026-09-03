using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The type of push-notification service the credential is for. Can be: <c>fcm</c>, <c>gcm</c>, or <c>apn</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CredentialEnumPushType>))]
public sealed record CredentialEnumPushType : StringEnum<CredentialEnumPushType>
{
    private CredentialEnumPushType(string value) : base(value)
    {
    }

    public static readonly CredentialEnumPushType Apn = new("apn");

    public static readonly CredentialEnumPushType Gcm = new("gcm");

    public static readonly CredentialEnumPushType Fcm = new("fcm");

    public static CredentialEnumPushType FromValue(string value) => FromValueCore(value);
}
