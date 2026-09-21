using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method we should use to call <c>hold_url</c>. Can be: <c>GET</c> or <c>POST</c> and the default is <c>GET</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<HoldMethod>))]
public sealed record HoldMethod : StringEnum<HoldMethod>
{
    private HoldMethod(string value) : base(value)
    {
    }

    public static readonly HoldMethod Get = new("GET");

    public static readonly HoldMethod Post = new("POST");

    public static HoldMethod FromValue(string value) => FromValueCore(value);
}
