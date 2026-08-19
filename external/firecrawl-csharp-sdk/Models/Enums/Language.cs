using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Language of the code to execute. Use <c>node</c> for JavaScript or <c>bash</c> for agent-browser CLI commands.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Language>))]
public sealed record Language : StringEnum<Language>
{
    private Language(string value) : base(value)
    {
    }

    public static readonly Language Python = new("python");

    public static readonly Language Node = new("node");

    public static readonly Language Bash = new("bash");

    public static Language FromValue(string value) => FromValueCore(value);
}
