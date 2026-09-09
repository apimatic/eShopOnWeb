using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// The type of payment collection to be used in the subscription. For legacy Statements Architecture valid options are - <c>invoice</c>, <c>automatic</c>. For current Relationship Invoicing Architecture valid options are - <c>remittance</c>, <c>automatic</c>, <c>prepaid</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CollectionMethod>))]
public sealed record CollectionMethod : StringEnum<CollectionMethod>
{
    private CollectionMethod(string value) : base(value)
    {
    }

    public static readonly CollectionMethod Automatic = new("automatic");

    public static readonly CollectionMethod Remittance = new("remittance");

    public static readonly CollectionMethod Prepaid = new("prepaid");

    public static readonly CollectionMethod Invoice = new("invoice");

    public static CollectionMethod FromValue(string value) => FromValueCore(value);
}
