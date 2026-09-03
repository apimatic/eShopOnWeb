using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The container format of the media files used by the compositions created by the composition hook. If <c>mp4</c> or <c>webm</c>, <c>audio_sources</c> must have one or more tracks and/or a <c>video_layout</c> element must contain a valid <c>video_sources</c> list, otherwise an error occurs.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CompositionHookEnumFormat>))]
public sealed record CompositionHookEnumFormat : StringEnum<CompositionHookEnumFormat>
{
    private CompositionHookEnumFormat(string value) : base(value)
    {
    }

    public static readonly CompositionHookEnumFormat Mp4 = new("mp4");

    public static readonly CompositionHookEnumFormat Webm = new("webm");

    public static CompositionHookEnumFormat FromValue(string value) => FromValueCore(value);
}
