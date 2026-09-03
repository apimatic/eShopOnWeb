using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method we should use to call <c>status_callback</c>. Can be: <c>GET</c> or <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<StatusCallbackMethod6>))]
public sealed record StatusCallbackMethod6 : StringEnum<StatusCallbackMethod6>
{
    private StatusCallbackMethod6(string value) : base(value)
    {
    }

    public static readonly StatusCallbackMethod6 Get = new("GET");

    public static readonly StatusCallbackMethod6 Post = new("POST");

    public static StatusCallbackMethod6 FromValue(string value) => FromValueCore(value);
}
