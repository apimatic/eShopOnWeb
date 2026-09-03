using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<NewFactorEnumNotificationPlatforms>))]
public sealed record NewFactorEnumNotificationPlatforms : StringEnum<NewFactorEnumNotificationPlatforms>
{
    private NewFactorEnumNotificationPlatforms(string value) : base(value)
    {
    }

    public static readonly NewFactorEnumNotificationPlatforms Apn = new("apn");

    public static readonly NewFactorEnumNotificationPlatforms Fcm = new("fcm");

    public static readonly NewFactorEnumNotificationPlatforms None = new("none");

    public static NewFactorEnumNotificationPlatforms FromValue(string value) => FromValueCore(value);
}
