using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method we should use when calling the <c>url</c>. Can be: <c>GET</c> or <c>POST</c> and the default is <c>POST</c>. If an <c>application_sid</c> parameter is present, this parameter is ignored.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Method1>))]
public sealed record Method1 : StringEnum<Method1>
{
    private Method1(string value) : base(value)
    {
    }

    public static readonly Method1 Get = new("GET");

    public static readonly Method1 Post = new("POST");

    public static Method1 FromValue(string value) => FromValueCore(value);
}
