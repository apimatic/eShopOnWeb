using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method Twilio uses when sending <c>status_callback</c> requests. Possible values are <c>GET</c> and <c>POST</c>. Default is <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<StatusCallbackMethod19>))]
public sealed record StatusCallbackMethod19 : StringEnum<StatusCallbackMethod19>
{
    private StatusCallbackMethod19(string value) : base(value)
    {
    }

    public static readonly StatusCallbackMethod19 Get = new("GET");

    public static readonly StatusCallbackMethod19 Post = new("POST");

    public static StatusCallbackMethod19 FromValue(string value) => FromValueCore(value);
}
