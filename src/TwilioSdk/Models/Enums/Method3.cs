using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method Twilio will use when requesting the above <c>Url</c>. Either <c>GET</c> or <c>POST</c>. Default is <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Method3>))]
public sealed record Method3 : StringEnum<Method3>
{
    private Method3(string value) : base(value)
    {
    }

    public static readonly Method3 Get = new("GET");

    public static readonly Method3 Post = new("POST");

    public static Method3 FromValue(string value) => FromValueCore(value);
}
