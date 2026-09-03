using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The type of customer account in the losing carrier. This should either be: 'Individual' or 'Business'., The type of End User the regulation requires - can be <c>Individual</c> or <c>Business</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CustomerType>))]
public sealed record CustomerType : StringEnum<CustomerType>
{
    private CustomerType(string value) : base(value)
    {
    }

    public static readonly CustomerType Business = new("Business");

    public static readonly CustomerType Individual = new("Individual");

    public static CustomerType FromValue(string value) => FromValueCore(value);
}
