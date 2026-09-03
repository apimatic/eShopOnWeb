using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method that we should use to request the <c>fallback_url</c>. Can be: <c>GET</c> or <c>POST</c> and the default is <c>POST</c>. If an <c>application_sid</c> parameter is present, this parameter is ignored.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<FallbackMethod>))]
public sealed record FallbackMethod : StringEnum<FallbackMethod>
{
    private FallbackMethod(string value) : base(value)
    {
    }

    public static readonly FallbackMethod Get = new("GET");

    public static readonly FallbackMethod Post = new("POST");

    public static FallbackMethod FromValue(string value) => FromValueCore(value);
}
