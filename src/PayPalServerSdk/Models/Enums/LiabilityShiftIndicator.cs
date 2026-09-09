using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Liability shift indicator. The outcome of the issuer's authentication.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<LiabilityShiftIndicator>))]
public sealed record LiabilityShiftIndicator : StringEnum<LiabilityShiftIndicator>
{
    private LiabilityShiftIndicator(string value) : base(value)
    {
    }

    /// <summary>
    /// Liability is with the merchant.
    /// </summary>
    public static readonly LiabilityShiftIndicator No = new("NO");

    /// <summary>
    /// Liability may shift to the card issuer.
    /// </summary>
    public static readonly LiabilityShiftIndicator Possible = new("POSSIBLE");

    /// <summary>
    /// The authentication system is not available.
    /// </summary>
    public static readonly LiabilityShiftIndicator Unknown = new("UNKNOWN");

    public static LiabilityShiftIndicator FromValue(string value) => FromValueCore(value);
}
