using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// The result of the comparison between the two page versions. 'new' means this page did not exist before, 'same' means content has not changed, 'changed' means content has changed, 'removed' means the page was removed.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ChangeStatus>))]
public sealed record ChangeStatus : StringEnum<ChangeStatus>
{
    private ChangeStatus(string value) : base(value)
    {
    }

    public static readonly ChangeStatus New = new("new");

    public static readonly ChangeStatus Same = new("same");

    public static readonly ChangeStatus Changed = new("changed");

    public static readonly ChangeStatus Removed = new("removed");

    public static ChangeStatus FromValue(string value) => FromValueCore(value);
}
