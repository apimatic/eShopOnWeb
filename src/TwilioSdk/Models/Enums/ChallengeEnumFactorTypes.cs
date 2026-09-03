using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The Factor Type of this Challenge. Currently <c>push</c> and <c>totp</c> are supported.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ChallengeEnumFactorTypes>))]
public sealed record ChallengeEnumFactorTypes : StringEnum<ChallengeEnumFactorTypes>
{
    private ChallengeEnumFactorTypes(string value) : base(value)
    {
    }

    public static readonly ChallengeEnumFactorTypes Push = new("push");

    public static readonly ChallengeEnumFactorTypes Totp = new("totp");

    public static readonly ChallengeEnumFactorTypes Passkeys = new("passkeys");

    public static ChallengeEnumFactorTypes FromValue(string value) => FromValueCore(value);
}
