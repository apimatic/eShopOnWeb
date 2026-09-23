using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Status31>))]
public sealed record Status31 : StringEnum<Status31>
{
    private Status31(string value) : base(value)
    {
    }

    public static readonly Status31 Active = new("ACTIVE");

    public static readonly Status31 Inactive = new("INACTIVE");

    public static readonly Status31 Closed = new("CLOSED");

    public static Status31 FromValue(string value) => FromValueCore(value);
}
