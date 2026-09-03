using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The Status of this Factor. One of <c>unverified</c> or <c>verified</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<FactorEnumFactorStatuses>))]
public sealed record FactorEnumFactorStatuses : StringEnum<FactorEnumFactorStatuses>
{
    private FactorEnumFactorStatuses(string value) : base(value)
    {
    }

    public static readonly FactorEnumFactorStatuses Unverified = new("unverified");

    public static readonly FactorEnumFactorStatuses Verified = new("verified");

    public static FactorEnumFactorStatuses FromValue(string value) => FromValueCore(value);
}
