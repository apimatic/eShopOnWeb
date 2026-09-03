using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method we should use when requesting the <c>status_callback</c> URL. Can be: <c>GET</c> or <c>POST</c> and the default is <c>POST</c>. If an <c>application_sid</c> parameter is present, this parameter is ignored.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<StatusCallbackMethod9>))]
public sealed record StatusCallbackMethod9 : StringEnum<StatusCallbackMethod9>
{
    private StatusCallbackMethod9(string value) : base(value)
    {
    }

    public static readonly StatusCallbackMethod9 Get = new("GET");

    public static readonly StatusCallbackMethod9 Post = new("POST");

    public static StatusCallbackMethod9 FromValue(string value) => FromValueCore(value);
}
