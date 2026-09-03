using System.Text.Json.Serialization;
using PayPal.Core.Enum;

namespace PayPal.Models.Enums;

/// <summary>
/// Verification status of Card.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CardVerificationStatus>))]
public sealed record CardVerificationStatus : StringEnum<CardVerificationStatus>
{
    private CardVerificationStatus(string value) : base(value)
    {
    }

    /// <summary>
    /// Card has been verified
    /// </summary>
    public static readonly CardVerificationStatus Verified = new("VERIFIED");

    /// <summary>
    /// Card verification has failed
    /// </summary>
    public static readonly CardVerificationStatus Failed = new("FAILED");

    public static CardVerificationStatus FromValue(string value) => FromValueCore(value);
}
