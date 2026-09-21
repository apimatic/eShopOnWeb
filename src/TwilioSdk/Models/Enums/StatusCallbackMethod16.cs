using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method we should use to call <c>status_callback</c>. Can be: <c>GET</c> and <c>POST</c> and defaults to <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<StatusCallbackMethod16>))]
public sealed record StatusCallbackMethod16 : StringEnum<StatusCallbackMethod16>
{
    private StatusCallbackMethod16(string value) : base(value)
    {
    }

    public static readonly StatusCallbackMethod16 Get = new("GET");

    public static readonly StatusCallbackMethod16 Post = new("POST");

    public static StatusCallbackMethod16 FromValue(string value) => FromValueCore(value);
}
