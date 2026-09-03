using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<AiInsightsEnumTagGroup>))]
public sealed record AiInsightsEnumTagGroup : StringEnum<AiInsightsEnumTagGroup>
{
    private AiInsightsEnumTagGroup(string value) : base(value)
    {
    }

    public static readonly AiInsightsEnumTagGroup Topics = new("topics");

    public static AiInsightsEnumTagGroup FromValue(string value) => FromValueCore(value);
}
