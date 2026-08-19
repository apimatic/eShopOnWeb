using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Types1>))]
public sealed record Types1 : StringEnum<Types1>
{
    private Types1(string value) : base(value)
    {
    }

    public static readonly Types1 Doc = new("doc");

    public static readonly Types1 Issue = new("issue");

    public static readonly Types1 PullRequest = new("pull_request");

    public static readonly Types1 Readme = new("readme");

    public static Types1 FromValue(string value) => FromValueCore(value);
}
