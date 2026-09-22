using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The Status of this Factor. One of <c>unverified</c> or <c>verified</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<NewFactorEnumFactorStatuses>))]
public sealed record NewFactorEnumFactorStatuses : StringEnum<NewFactorEnumFactorStatuses>
{
    private NewFactorEnumFactorStatuses(string value) : base(value)
    {
    }

    public static readonly NewFactorEnumFactorStatuses Unverified = new("unverified");

    public static readonly NewFactorEnumFactorStatuses Verified = new("verified");

    public static NewFactorEnumFactorStatuses FromValue(string value) => FromValueCore(value);
}
