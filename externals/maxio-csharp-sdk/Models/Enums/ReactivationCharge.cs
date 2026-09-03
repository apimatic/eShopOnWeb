using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

/// <summary>
/// You may choose how to handle the reactivation charge for that subscription: 1) <c>prorated</c> A prorated charge for the product price will be attempted to complete the period 2) <c>immediate</c> A full-price charge for the product price will be attempted immediately 3) <c>delayed</c> A full-price charge for the product price will be attempted at the next renewal.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ReactivationCharge>))]
public sealed record ReactivationCharge : StringEnum<ReactivationCharge>
{
    private ReactivationCharge(string value) : base(value)
    {
    }

    public static readonly ReactivationCharge Prorated = new("prorated");

    public static readonly ReactivationCharge Immediate = new("immediate");

    public static readonly ReactivationCharge Delayed = new("delayed");

    public static ReactivationCharge FromValue(string value) => FromValueCore(value);
}
