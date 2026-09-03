using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Type of Address, value can be <c>whatsapp</c> or <c>sms</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ConfigurationAddressEnumType>))]
public sealed record ConfigurationAddressEnumType : StringEnum<ConfigurationAddressEnumType>
{
    private ConfigurationAddressEnumType(string value) : base(value)
    {
    }

    public static readonly ConfigurationAddressEnumType Sms = new("sms");

    public static readonly ConfigurationAddressEnumType Whatsapp = new("whatsapp");

    public static readonly ConfigurationAddressEnumType Messenger = new("messenger");

    public static readonly ConfigurationAddressEnumType Gbm = new("gbm");

    public static readonly ConfigurationAddressEnumType Email = new("email");

    public static readonly ConfigurationAddressEnumType Rcs = new("rcs");

    public static readonly ConfigurationAddressEnumType Apple = new("apple");

    public static readonly ConfigurationAddressEnumType Chat = new("chat");

    public static ConfigurationAddressEnumType FromValue(string value) => FromValueCore(value);
}
