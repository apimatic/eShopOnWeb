using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The push technology to use for the Binding. Can be: <c>apn</c>, <c>gcm</c>, <c>fcm</c>, or <c>twilsock</c>.  See <see href="https://www.twilio.com/docs/chat/push-notification-configuration">push notification configuration</see> for more info.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ServiceBindingEnumBindingType>))]
public sealed record ServiceBindingEnumBindingType : StringEnum<ServiceBindingEnumBindingType>
{
    private ServiceBindingEnumBindingType(string value) : base(value)
    {
    }

    public static readonly ServiceBindingEnumBindingType Apn = new("apn");

    public static readonly ServiceBindingEnumBindingType Gcm = new("gcm");

    public static readonly ServiceBindingEnumBindingType Fcm = new("fcm");

    public static readonly ServiceBindingEnumBindingType Twilsock = new("twilsock");

    public static ServiceBindingEnumBindingType FromValue(string value) => FromValueCore(value);
}
