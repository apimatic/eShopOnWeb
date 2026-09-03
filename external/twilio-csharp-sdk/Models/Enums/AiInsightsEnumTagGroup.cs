using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<AiInsightsEnumTagGroup>))]
public sealed record AiInsightsEnumTagGroup : StringEnum<AiInsightsEnumTagGroup>
{
    private AiInsightsEnumTagGroup(string value) : base(value)
    {
    }

    public static readonly AiInsightsEnumTagGroup Topics = new("topics");

    public static AiInsightsEnumTagGroup FromValue(string value) => FromValueCore(value);
}
