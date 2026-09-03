using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<BundleEnumEndUserType>))]
public sealed record BundleEnumEndUserType : StringEnum<BundleEnumEndUserType>
{
    private BundleEnumEndUserType(string value) : base(value)
    {
    }

    public static readonly BundleEnumEndUserType Individual = new("individual");

    public static readonly BundleEnumEndUserType Business = new("business");

    public static BundleEnumEndUserType FromValue(string value) => FromValueCore(value);
}
