using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method for the webhook.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CallbackMethod2>))]
public sealed record CallbackMethod2 : StringEnum<CallbackMethod2>
{
    private CallbackMethod2(string value) : base(value)
    {
    }

    public static readonly CallbackMethod2 Post = new("POST");

    public static readonly CallbackMethod2 Put = new("PUT");

    public static CallbackMethod2 FromValue(string value) => FromValueCore(value);
}
