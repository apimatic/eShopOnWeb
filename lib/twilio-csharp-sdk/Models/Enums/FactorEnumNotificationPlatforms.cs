using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<FactorEnumNotificationPlatforms>))]
public sealed record FactorEnumNotificationPlatforms : StringEnum<FactorEnumNotificationPlatforms>
{
    private FactorEnumNotificationPlatforms(string value) : base(value)
    {
    }

    public static readonly FactorEnumNotificationPlatforms Apn = new("apn");

    public static readonly FactorEnumNotificationPlatforms Fcm = new("fcm");

    public static readonly FactorEnumNotificationPlatforms None = new("none");

    public static FactorEnumNotificationPlatforms FromValue(string value) => FromValueCore(value);
}
