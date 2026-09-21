using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<VideoRoomSummaryEnumEdgeLocation>))]
public sealed record VideoRoomSummaryEnumEdgeLocation : StringEnum<VideoRoomSummaryEnumEdgeLocation>
{
    private VideoRoomSummaryEnumEdgeLocation(string value) : base(value)
    {
    }

    public static readonly VideoRoomSummaryEnumEdgeLocation Ashburn = new("ashburn");

    public static readonly VideoRoomSummaryEnumEdgeLocation Dublin = new("dublin");

    public static readonly VideoRoomSummaryEnumEdgeLocation Frankfurt = new("frankfurt");

    public static readonly VideoRoomSummaryEnumEdgeLocation Singapore = new("singapore");

    public static readonly VideoRoomSummaryEnumEdgeLocation Sydney = new("sydney");

    public static readonly VideoRoomSummaryEnumEdgeLocation SaoPaulo = new("sao_paulo");

    public static readonly VideoRoomSummaryEnumEdgeLocation Roaming = new("roaming");

    public static readonly VideoRoomSummaryEnumEdgeLocation Umatilla = new("umatilla");

    public static readonly VideoRoomSummaryEnumEdgeLocation Tokyo = new("tokyo");

    public static VideoRoomSummaryEnumEdgeLocation FromValue(string value) => FromValueCore(value);
}
