using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method used to invoke the webhook URL.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Method11>))]
public sealed record Method11 : StringEnum<Method11>
{
    private Method11(string value) : base(value)
    {
    }

    public static readonly Method11 Post = new("POST");

    public static readonly Method11 Get = new("GET");

    public static readonly Method11 Put = new("PUT");

    public static readonly Method11 Delete = new("DELETE");

    public static readonly Method11 Patch = new("PATCH");

    public static Method11 FromValue(string value) => FromValueCore(value);
}
