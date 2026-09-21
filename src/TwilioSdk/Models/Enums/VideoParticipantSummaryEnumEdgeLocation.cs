using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<VideoParticipantSummaryEnumEdgeLocation>))]
public sealed record VideoParticipantSummaryEnumEdgeLocation : StringEnum<VideoParticipantSummaryEnumEdgeLocation>
{
    private VideoParticipantSummaryEnumEdgeLocation(string value) : base(value)
    {
    }

    public static readonly VideoParticipantSummaryEnumEdgeLocation Ashburn = new("ashburn");

    public static readonly VideoParticipantSummaryEnumEdgeLocation Dublin = new("dublin");

    public static readonly VideoParticipantSummaryEnumEdgeLocation Frankfurt = new("frankfurt");

    public static readonly VideoParticipantSummaryEnumEdgeLocation Singapore = new("singapore");

    public static readonly VideoParticipantSummaryEnumEdgeLocation Sydney = new("sydney");

    public static readonly VideoParticipantSummaryEnumEdgeLocation SaoPaulo = new("sao_paulo");

    public static readonly VideoParticipantSummaryEnumEdgeLocation Roaming = new("roaming");

    public static readonly VideoParticipantSummaryEnumEdgeLocation Umatilla = new("umatilla");

    public static readonly VideoParticipantSummaryEnumEdgeLocation Tokyo = new("tokyo");

    public static VideoParticipantSummaryEnumEdgeLocation FromValue(string value) => FromValueCore(value);
}
