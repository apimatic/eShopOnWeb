using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method we use to call <c>status_callback</c>. Can be: <c>GET</c> or <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<StatusCallbackMethod>))]
public sealed record StatusCallbackMethod : StringEnum<StatusCallbackMethod>
{
    private StatusCallbackMethod(string value) : base(value)
    {
    }

    public static readonly StatusCallbackMethod Get = new("GET");

    public static readonly StatusCallbackMethod Post = new("POST");

    public static StatusCallbackMethod FromValue(string value) => FromValueCore(value);
}
