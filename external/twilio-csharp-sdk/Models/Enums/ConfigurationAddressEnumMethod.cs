using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ConfigurationAddressEnumMethod>))]
public sealed record ConfigurationAddressEnumMethod : StringEnum<ConfigurationAddressEnumMethod>
{
    private ConfigurationAddressEnumMethod(string value) : base(value)
    {
    }

    public static readonly ConfigurationAddressEnumMethod Get = new("get");

    public static readonly ConfigurationAddressEnumMethod Post = new("post");

    public static ConfigurationAddressEnumMethod FromValue(string value) => FromValueCore(value);
}
