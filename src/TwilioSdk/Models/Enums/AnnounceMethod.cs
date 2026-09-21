using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method used to call <c>announce_url</c>. Can be: <c>GET</c> or <c>POST</c> and the default is <c>POST</c>
/// </summary>
[JsonConverter(typeof(StringEnumConverter<AnnounceMethod>))]
public sealed record AnnounceMethod : StringEnum<AnnounceMethod>
{
    private AnnounceMethod(string value) : base(value)
    {
    }

    public static readonly AnnounceMethod Get = new("GET");

    public static readonly AnnounceMethod Post = new("POST");

    public static AnnounceMethod FromValue(string value) => FromValueCore(value);
}
