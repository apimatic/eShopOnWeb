using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method we use to call <c>callback_url</c>. Can be: <c>GET</c> or <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CallbackMethod>))]
public sealed record CallbackMethod : StringEnum<CallbackMethod>
{
    private CallbackMethod(string value) : base(value)
    {
    }

    public static readonly CallbackMethod Get = new("GET");

    public static readonly CallbackMethod Post = new("POST");

    public static CallbackMethod FromValue(string value) => FromValueCore(value);
}
