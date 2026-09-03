using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ChallengeEnumListOrders>))]
public sealed record ChallengeEnumListOrders : StringEnum<ChallengeEnumListOrders>
{
    private ChallengeEnumListOrders(string value) : base(value)
    {
    }

    public static readonly ChallengeEnumListOrders Asc = new("asc");

    public static readonly ChallengeEnumListOrders Desc = new("desc");

    public static ChallengeEnumListOrders FromValue(string value) => FromValueCore(value);
}
