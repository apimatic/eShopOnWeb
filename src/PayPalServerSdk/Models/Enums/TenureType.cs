using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The tenure type of the billing cycle identifies if the billing cycle is a trial(free or discounted) or regular billing cycle., The tenure type of the billing cycle. In case of a plan having trial cycle, only 2 trial cycles are allowed per plan., The type of the billing cycle.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<TenureType>))]
public sealed record TenureType : StringEnum<TenureType>
{
    private TenureType(string value) : base(value)
    {
    }

    /// <summary>
    /// A regular billing cycle to identify recurring charges for the billing agreement.
    /// </summary>
    public static readonly TenureType Regular = new("REGULAR");

    /// <summary>
    /// A trial billing cycle to identify free or discounted charge for the billing agreement. Free trails will not have a price object in pricing scheme where as a discounted trial would have a discounted price compared to regular billing cycle.
    /// </summary>
    public static readonly TenureType Trial = new("TRIAL");

    public static TenureType FromValue(string value) => FromValueCore(value);
}
