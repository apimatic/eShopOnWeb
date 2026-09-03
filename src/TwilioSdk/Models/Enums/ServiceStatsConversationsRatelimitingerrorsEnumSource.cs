using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Source via which the request came from. Can be sdk, api.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ServiceStatsConversationsRatelimitingerrorsEnumSource>))]
public sealed record ServiceStatsConversationsRatelimitingerrorsEnumSource : StringEnum<ServiceStatsConversationsRatelimitingerrorsEnumSource>
{
    private ServiceStatsConversationsRatelimitingerrorsEnumSource(string value) : base(value)
    {
    }

    public static readonly ServiceStatsConversationsRatelimitingerrorsEnumSource Sdk = new("SDK");

    public static readonly ServiceStatsConversationsRatelimitingerrorsEnumSource Api = new("API");

    public static ServiceStatsConversationsRatelimitingerrorsEnumSource FromValue(string value) =>
        FromValueCore(value);
}
