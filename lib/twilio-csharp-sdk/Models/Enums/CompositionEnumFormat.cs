using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The container format of the composition's media files as specified in the POST request that created the Composition resource. See <see href="https://www.twilio.com/docs/video/api/compositions-resource#http-post-parameters">POST Parameters</see> for more information.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CompositionEnumFormat>))]
public sealed record CompositionEnumFormat : StringEnum<CompositionEnumFormat>
{
    private CompositionEnumFormat(string value) : base(value)
    {
    }

    public static readonly CompositionEnumFormat Mp4 = new("mp4");

    public static readonly CompositionEnumFormat Webm = new("webm");

    public static CompositionEnumFormat FromValue(string value) => FromValueCore(value);
}
