using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<BundleCopyEnumEndUserType>))]
public sealed record BundleCopyEnumEndUserType : StringEnum<BundleCopyEnumEndUserType>
{
    private BundleCopyEnumEndUserType(string value) : base(value)
    {
    }

    public static readonly BundleCopyEnumEndUserType Individual = new("individual");

    public static readonly BundleCopyEnumEndUserType Business = new("business");

    public static BundleCopyEnumEndUserType FromValue(string value) => FromValueCore(value);
}
