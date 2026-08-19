using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<PullRequest>))]
public sealed record PullRequest : StringEnum<PullRequest>
{
    private PullRequest(string value) : base(value)
    {
    }

    public static readonly PullRequest Ok = new("ok");

    public static readonly PullRequest Degraded = new("degraded");

    public static readonly PullRequest Unavailable = new("unavailable");

    public static readonly PullRequest Skipped = new("skipped");

    public static PullRequest FromValue(string value) => FromValueCore(value);
}
