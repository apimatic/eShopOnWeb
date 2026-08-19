using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Result kind.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Type39>))]
public sealed record Type39 : StringEnum<Type39>
{
    private Type39(string value) : base(value)
    {
    }

    public static readonly Type39 Doc = new("doc");

    public static readonly Type39 Issue = new("issue");

    public static readonly Type39 PullRequest = new("pull_request");

    public static readonly Type39 Readme = new("readme");

    public static Type39 FromValue(string value) => FromValueCore(value);
}
