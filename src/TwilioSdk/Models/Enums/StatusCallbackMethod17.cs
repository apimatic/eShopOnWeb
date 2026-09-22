using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The http method for the status_callback (one of GET, POST).
/// </summary>
[JsonConverter(typeof(StringEnumConverter<StatusCallbackMethod17>))]
public sealed record StatusCallbackMethod17 : StringEnum<StatusCallbackMethod17>
{
    private StatusCallbackMethod17(string value) : base(value)
    {
    }

    public static readonly StatusCallbackMethod17 Get = new("GET");

    public static readonly StatusCallbackMethod17 Post = new("POST");

    public static StatusCallbackMethod17 FromValue(string value) => FromValueCore(value);
}
