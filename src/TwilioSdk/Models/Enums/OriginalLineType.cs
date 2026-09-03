using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The original line type
/// </summary>
[JsonConverter(typeof(StringEnumConverter<OriginalLineType>))]
public sealed record OriginalLineType : StringEnum<OriginalLineType>
{
    private OriginalLineType(string value) : base(value)
    {
    }

    public static readonly OriginalLineType Mobile = new("mobile");

    public static readonly OriginalLineType Landline = new("landline");

    public static readonly OriginalLineType TollFree = new("tollFree");

    public static readonly OriginalLineType FixedVoip = new("fixedVoip");

    public static readonly OriginalLineType NonFixedVoip = new("nonFixedVoip");

    public static readonly OriginalLineType Personal = new("personal");

    public static readonly OriginalLineType Premium = new("premium");

    public static readonly OriginalLineType Voicemail = new("voicemail");

    public static readonly OriginalLineType SharedCost = new("sharedCost");

    public static readonly OriginalLineType Uan = new("uan");

    public static readonly OriginalLineType Pager = new("pager");

    public static readonly OriginalLineType Unknown = new("unknown");

    public static OriginalLineType FromValue(string value) => FromValueCore(value);
}
