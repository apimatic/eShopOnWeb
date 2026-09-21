using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method we should use when calling the <c>async_amd_status_callback</c> URL. Can be: <c>GET</c> or <c>POST</c> and the default is <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<AsyncAmdStatusCallbackMethod>))]
public sealed record AsyncAmdStatusCallbackMethod : StringEnum<AsyncAmdStatusCallbackMethod>
{
    private AsyncAmdStatusCallbackMethod(string value) : base(value)
    {
    }

    public static readonly AsyncAmdStatusCallbackMethod Get = new("GET");

    public static readonly AsyncAmdStatusCallbackMethod Post = new("POST");

    public static AsyncAmdStatusCallbackMethod FromValue(string value) => FromValueCore(value);
}
