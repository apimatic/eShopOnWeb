using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method for the fallback webhook.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<FallbackMethod1>))]
public sealed record FallbackMethod1 : StringEnum<FallbackMethod1>
{
    private FallbackMethod1(string value) : base(value)
    {
    }

    public static readonly FallbackMethod1 Post = new("POST");

    public static readonly FallbackMethod1 Put = new("PUT");

    public static FallbackMethod1 FromValue(string value) => FromValueCore(value);
}
