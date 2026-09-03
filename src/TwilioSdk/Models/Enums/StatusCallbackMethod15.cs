using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method we should use to call <c>status_callback</c>. Can be: <c>GET</c> or <c>POST</c>, and the default is <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<StatusCallbackMethod15>))]
public sealed record StatusCallbackMethod15 : StringEnum<StatusCallbackMethod15>
{
    private StatusCallbackMethod15(string value) : base(value)
    {
    }

    public static readonly StatusCallbackMethod15 Get = new("GET");

    public static readonly StatusCallbackMethod15 Post = new("POST");

    public static StatusCallbackMethod15 FromValue(string value) => FromValueCore(value);
}
