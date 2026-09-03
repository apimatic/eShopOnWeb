using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

/// <summary>
/// The type of entry
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ServiceCreditType>))]
public sealed record ServiceCreditType : StringEnum<ServiceCreditType>
{
    private ServiceCreditType(string value) : base(value)
    {
    }

    public static readonly ServiceCreditType Credit = new("Credit");

    public static readonly ServiceCreditType Debit = new("Debit");

    public static ServiceCreditType FromValue(string value) => FromValueCore(value);
}
