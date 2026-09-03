using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The third-party political vetting provider.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<TollfreeVerificationEnumVettingProvider>))]
public sealed record TollfreeVerificationEnumVettingProvider : StringEnum<TollfreeVerificationEnumVettingProvider>
{
    private TollfreeVerificationEnumVettingProvider(string value) : base(value)
    {
    }

    public static readonly TollfreeVerificationEnumVettingProvider CampaignVerify = new("CAMPAIGN_VERIFY");

    public static TollfreeVerificationEnumVettingProvider FromValue(string value) => FromValueCore(value);
}
