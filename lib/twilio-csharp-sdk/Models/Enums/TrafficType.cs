using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<TrafficType>))]
public sealed record TrafficType : StringEnum<TrafficType>
{
    private TrafficType(string value) : base(value)
    {
    }

    public static readonly TrafficType Transactional = new("TRANSACTIONAL");

    public static readonly TrafficType Promotional = new("PROMOTIONAL");

    public static readonly TrafficType Both = new("BOTH");

    public static TrafficType FromValue(string value) => FromValueCore(value);
}
