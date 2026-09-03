using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method we should use to call <c>announce_url</c>. Can be: <c>GET</c> or <c>POST</c> and defaults to <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<AnnounceMethod1>))]
public sealed record AnnounceMethod1 : StringEnum<AnnounceMethod1>
{
    private AnnounceMethod1(string value) : base(value)
    {
    }

    public static readonly AnnounceMethod1 Get = new("GET");

    public static readonly AnnounceMethod1 Post = new("POST");

    public static AnnounceMethod1 FromValue(string value) => FromValueCore(value);
}
