using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Reserved.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ServiceEnumScanMessageContent>))]
public sealed record ServiceEnumScanMessageContent : StringEnum<ServiceEnumScanMessageContent>
{
    private ServiceEnumScanMessageContent(string value) : base(value)
    {
    }

    public static readonly ServiceEnumScanMessageContent Inherit = new("inherit");

    public static readonly ServiceEnumScanMessageContent Enable = new("enable");

    public static readonly ServiceEnumScanMessageContent Disable = new("disable");

    public static ServiceEnumScanMessageContent FromValue(string value) => FromValueCore(value);
}
