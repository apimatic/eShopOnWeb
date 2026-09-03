using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Method21>))]
public sealed record Method21 : StringEnum<Method21>
{
    private Method21(string value) : base(value)
    {
    }

    public static readonly Method21 Post = new("POST");

    public static readonly Method21 Get = new("GET");

    public static readonly Method21 Put = new("PUT");

    public static readonly Method21 Delete = new("DELETE");

    public static readonly Method21 Patch = new("PATCH");

    public static Method21 FromValue(string value) => FromValueCore(value);
}
