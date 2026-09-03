using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method we should use to call <c>callback_url</c>. Can be: <c>GET</c> or <c>POST</c> and the default is <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CallbackMethod1>))]
public sealed record CallbackMethod1 : StringEnum<CallbackMethod1>
{
    private CallbackMethod1(string value) : base(value)
    {
    }

    public static readonly CallbackMethod1 Get = new("GET");

    public static readonly CallbackMethod1 Post = new("POST");

    public static CallbackMethod1 FromValue(string value) => FromValueCore(value);
}
