using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method we use to call <c>deauthorize_callback_url</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<DeauthorizeCallbackMethod>))]
public sealed record DeauthorizeCallbackMethod : StringEnum<DeauthorizeCallbackMethod>
{
    private DeauthorizeCallbackMethod(string value) : base(value)
    {
    }

    public static readonly DeauthorizeCallbackMethod Get = new("GET");

    public static readonly DeauthorizeCallbackMethod Post = new("POST");

    public static DeauthorizeCallbackMethod FromValue(string value) => FromValueCore(value);
}
