using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method to use when calling <c>deauthorize_callback_url</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<DeauthorizeCallbackMethod1>))]
public sealed record DeauthorizeCallbackMethod1 : StringEnum<DeauthorizeCallbackMethod1>
{
    private DeauthorizeCallbackMethod1(string value) : base(value)
    {
    }

    public static readonly DeauthorizeCallbackMethod1 Get = new("GET");

    public static readonly DeauthorizeCallbackMethod1 Post = new("POST");

    public static DeauthorizeCallbackMethod1 FromValue(string value) => FromValueCore(value);
}
