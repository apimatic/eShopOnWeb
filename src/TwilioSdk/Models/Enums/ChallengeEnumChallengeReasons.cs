using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Reason for the Challenge to be in certain <c>status</c>. One of <c>none</c>, <c>not_needed</c> or <c>not_requested</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ChallengeEnumChallengeReasons>))]
public sealed record ChallengeEnumChallengeReasons : StringEnum<ChallengeEnumChallengeReasons>
{
    private ChallengeEnumChallengeReasons(string value) : base(value)
    {
    }

    public static readonly ChallengeEnumChallengeReasons None = new("none");

    public static readonly ChallengeEnumChallengeReasons NotNeeded = new("not_needed");

    public static readonly ChallengeEnumChallengeReasons NotRequested = new("not_requested");

    public static ChallengeEnumChallengeReasons FromValue(string value) => FromValueCore(value);
}
