using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The new line type after the override
/// </summary>
[JsonConverter(typeof(StringEnumConverter<OverriddenLineType>))]
public sealed record OverriddenLineType : StringEnum<OverriddenLineType>
{
    private OverriddenLineType(string value) : base(value)
    {
    }

    public static readonly OverriddenLineType Mobile = new("mobile");

    public static readonly OverriddenLineType Landline = new("landline");

    public static readonly OverriddenLineType TollFree = new("tollFree");

    public static readonly OverriddenLineType FixedVoip = new("fixedVoip");

    public static readonly OverriddenLineType NonFixedVoip = new("nonFixedVoip");

    public static readonly OverriddenLineType Personal = new("personal");

    public static readonly OverriddenLineType Premium = new("premium");

    public static readonly OverriddenLineType Voicemail = new("voicemail");

    public static readonly OverriddenLineType SharedCost = new("sharedCost");

    public static readonly OverriddenLineType Uan = new("uan");

    public static readonly OverriddenLineType Pager = new("pager");

    public static readonly OverriddenLineType Unknown = new("unknown");

    public static OverriddenLineType FromValue(string value) => FromValueCore(value);
}
