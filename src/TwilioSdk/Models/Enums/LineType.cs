using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The new line type to override the original line type
/// </summary>
[JsonConverter(typeof(StringEnumConverter<LineType>))]
public sealed record LineType : StringEnum<LineType>
{
    private LineType(string value) : base(value)
    {
    }

    public static readonly LineType Mobile = new("mobile");

    public static readonly LineType Landline = new("landline");

    public static readonly LineType TollFree = new("tollFree");

    public static readonly LineType FixedVoip = new("fixedVoip");

    public static readonly LineType NonFixedVoip = new("nonFixedVoip");

    public static readonly LineType Personal = new("personal");

    public static readonly LineType Premium = new("premium");

    public static readonly LineType Voicemail = new("voicemail");

    public static readonly LineType SharedCost = new("sharedCost");

    public static readonly LineType Uan = new("uan");

    public static readonly LineType Pager = new("pager");

    public static readonly LineType Unknown = new("unknown");

    public static LineType FromValue(string value) => FromValueCore(value);
}
