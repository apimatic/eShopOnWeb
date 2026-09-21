using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<BundleEnumSortBy>))]
public sealed record BundleEnumSortBy : StringEnum<BundleEnumSortBy>
{
    private BundleEnumSortBy(string value) : base(value)
    {
    }

    public static readonly BundleEnumSortBy ValidUntil = new("valid-until");

    public static readonly BundleEnumSortBy DateUpdated = new("date-updated");

    public static BundleEnumSortBy FromValue(string value) => FromValueCore(value);
}
