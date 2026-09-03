using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The status of the Engagement. Can be: <c>active</c> or <c>ended</c>., The status of the Execution. Can be: <c>active</c> or <c>ended</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<EngagementEnumStatus>))]
public sealed record EngagementEnumStatus : StringEnum<EngagementEnumStatus>
{
    private EngagementEnumStatus(string value) : base(value)
    {
    }

    public static readonly EngagementEnumStatus Active = new("active");

    public static readonly EngagementEnumStatus Ended = new("ended");

    public static EngagementEnumStatus FromValue(string value) => FromValueCore(value);
}
