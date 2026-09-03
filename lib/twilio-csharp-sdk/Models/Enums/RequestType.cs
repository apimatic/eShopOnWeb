using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<RequestType>))]
public sealed record RequestType : StringEnum<RequestType>
{
    private RequestType(string value) : base(value)
    {
    }

    public static readonly RequestType New = new("NEW");

    public static readonly RequestType Migration = new("MIGRATION");

    public static readonly RequestType Lease = new("LEASE");

    public static RequestType FromValue(string value) => FromValueCore(value);
}
