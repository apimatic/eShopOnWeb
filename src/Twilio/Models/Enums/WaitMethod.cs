using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method we should use to call <c>wait_url</c>. Can be <c>GET</c> or <c>POST</c> and the default is <c>POST</c>. When using a static audio file, this should be <c>GET</c> so that we can cache the file.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<WaitMethod>))]
public sealed record WaitMethod : StringEnum<WaitMethod>
{
    private WaitMethod(string value) : base(value)
    {
    }

    public static readonly WaitMethod Get = new("GET");

    public static readonly WaitMethod Post = new("POST");

    public static WaitMethod FromValue(string value) => FromValueCore(value);
}
