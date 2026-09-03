using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// DEPRECATED. Feedback on how to improve brand score
/// </summary>
[JsonConverter(typeof(StringEnumConverter<BrandRegistrationsEnumBrandFeedback>))]
public sealed record BrandRegistrationsEnumBrandFeedback : StringEnum<BrandRegistrationsEnumBrandFeedback>
{
    private BrandRegistrationsEnumBrandFeedback(string value) : base(value)
    {
    }

    public static readonly BrandRegistrationsEnumBrandFeedback TaxId = new("TAX_ID");

    public static readonly BrandRegistrationsEnumBrandFeedback StockSymbol = new("STOCK_SYMBOL");

    public static readonly BrandRegistrationsEnumBrandFeedback Nonprofit = new("NONPROFIT");

    public static readonly BrandRegistrationsEnumBrandFeedback GovernmentEntity = new("GOVERNMENT_ENTITY");

    public static readonly BrandRegistrationsEnumBrandFeedback Others = new("OTHERS");

    public static BrandRegistrationsEnumBrandFeedback FromValue(string value) => FromValueCore(value);
}
