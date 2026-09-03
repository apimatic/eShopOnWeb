using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ConfigurationAddressEnumAutoCreationType>))]
public sealed record ConfigurationAddressEnumAutoCreationType : StringEnum<ConfigurationAddressEnumAutoCreationType>
{
    private ConfigurationAddressEnumAutoCreationType(string value) : base(value)
    {
    }

    public static readonly ConfigurationAddressEnumAutoCreationType Webhook = new("webhook");

    public static readonly ConfigurationAddressEnumAutoCreationType Studio = new("studio");

    public static readonly ConfigurationAddressEnumAutoCreationType Default = new("default");

    public static ConfigurationAddressEnumAutoCreationType FromValue(string value) => FromValueCore(value);
}
