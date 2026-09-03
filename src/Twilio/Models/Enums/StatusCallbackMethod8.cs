using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method we should use when calling the <c>status_callback</c> URL. Can be: <c>GET</c> or <c>POST</c> and the default is <c>POST</c>. If an <c>application_sid</c> parameter is present, this parameter is ignored.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<StatusCallbackMethod8>))]
public sealed record StatusCallbackMethod8 : StringEnum<StatusCallbackMethod8>
{
    private StatusCallbackMethod8(string value) : base(value)
    {
    }

    public static readonly StatusCallbackMethod8 Get = new("GET");

    public static readonly StatusCallbackMethod8 Post = new("POST");

    public static StatusCallbackMethod8 FromValue(string value) => FromValueCore(value);
}
