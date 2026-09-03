using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<DiscoverableCredentials>))]
public sealed record DiscoverableCredentials : StringEnum<DiscoverableCredentials>
{
    private DiscoverableCredentials(string value) : base(value)
    {
    }

    public static readonly DiscoverableCredentials Required = new("required");

    public static readonly DiscoverableCredentials Preferred = new("preferred");

    public static readonly DiscoverableCredentials Discouraged = new("discouraged");

    public static DiscoverableCredentials FromValue(string value) => FromValueCore(value);
}
