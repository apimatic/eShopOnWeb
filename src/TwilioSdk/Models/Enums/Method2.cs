using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// How to pass the update request data. Can be <c>GET</c> or <c>POST</c> and the default is <c>POST</c>. <c>POST</c> sends the data as encoded form data and <c>GET</c> sends the data as query parameters.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Method2>))]
public sealed record Method2 : StringEnum<Method2>
{
    private Method2(string value) : base(value)
    {
    }

    public static readonly Method2 Get = new("GET");

    public static readonly Method2 Post = new("POST");

    public static Method2 FromValue(string value) => FromValueCore(value);
}
