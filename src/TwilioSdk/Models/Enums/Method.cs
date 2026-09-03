using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method we should use when calling the <c>url</c> parameter's value. Can be: <c>GET</c> or <c>POST</c> and the default is <c>POST</c>. If an <c>application_sid</c> parameter is present, this parameter is ignored.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Method>))]
public sealed record Method : StringEnum<Method>
{
    private Method(string value) : base(value)
    {
    }

    public static readonly Method Get = new("GET");

    public static readonly Method Post = new("POST");

    public static Method FromValue(string value) => FromValueCore(value);
}
