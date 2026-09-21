using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The Status of this Challenge. One of <c>pending</c>, <c>expired</c>, <c>approved</c> or <c>denied</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ChallengeEnumChallengeStatuses>))]
public sealed record ChallengeEnumChallengeStatuses : StringEnum<ChallengeEnumChallengeStatuses>
{
    private ChallengeEnumChallengeStatuses(string value) : base(value)
    {
    }

    public static readonly ChallengeEnumChallengeStatuses Pending = new("pending");

    public static readonly ChallengeEnumChallengeStatuses Expired = new("expired");

    public static readonly ChallengeEnumChallengeStatuses Approved = new("approved");

    public static readonly ChallengeEnumChallengeStatuses Denied = new("denied");

    public static ChallengeEnumChallengeStatuses FromValue(string value) => FromValueCore(value);
}
