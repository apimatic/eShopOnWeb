using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method we should use to call <c>status_callback</c>. Can be: <c>GET</c> or <c>POST</c> and defaults to <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<StatusCallbackMethod10>))]
public sealed record StatusCallbackMethod10 : StringEnum<StatusCallbackMethod10>
{
    private StatusCallbackMethod10(string value) : base(value)
    {
    }

    public static readonly StatusCallbackMethod10 Get = new("GET");

    public static readonly StatusCallbackMethod10 Post = new("POST");

    public static StatusCallbackMethod10 FromValue(string value) => FromValueCore(value);
}
