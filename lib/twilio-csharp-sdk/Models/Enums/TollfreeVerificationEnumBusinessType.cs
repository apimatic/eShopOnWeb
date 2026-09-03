using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The type of business, valid values are PRIVATE_PROFIT, PUBLIC_PROFIT, NON_PROFIT, SOLE_PROPRIETOR, GOVERNMENT. Required field., Type of Business.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<TollfreeVerificationEnumBusinessType>))]
public sealed record TollfreeVerificationEnumBusinessType : StringEnum<TollfreeVerificationEnumBusinessType>
{
    private TollfreeVerificationEnumBusinessType(string value) : base(value)
    {
    }

    public static readonly TollfreeVerificationEnumBusinessType PrivateProfit = new("PRIVATE_PROFIT");

    public static readonly TollfreeVerificationEnumBusinessType PublicProfit = new("PUBLIC_PROFIT");

    public static readonly TollfreeVerificationEnumBusinessType SoleProprietor = new("SOLE_PROPRIETOR");

    public static readonly TollfreeVerificationEnumBusinessType NonProfit = new("NON_PROFIT");

    public static readonly TollfreeVerificationEnumBusinessType Government = new("GOVERNMENT");

    public static TollfreeVerificationEnumBusinessType FromValue(string value) => FromValueCore(value);
}
