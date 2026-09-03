using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The third-party provider that has conducted the vetting. One of “CampaignVerify” (Campaign Verify tokens) or “AEGIS” (Secondary Vetting).
/// </summary>
[JsonConverter(typeof(StringEnumConverter<BrandVettingEnumVettingProvider>))]
public sealed record BrandVettingEnumVettingProvider : StringEnum<BrandVettingEnumVettingProvider>
{
    private BrandVettingEnumVettingProvider(string value) : base(value)
    {
    }

    public static readonly BrandVettingEnumVettingProvider CampaignVerify = new("campaign-verify");

    public static readonly BrandVettingEnumVettingProvider Aegis = new("aegis");

    public static BrandVettingEnumVettingProvider FromValue(string value) => FromValueCore(value);
}
