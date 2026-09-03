using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record PaymentMethodCreditCard
{
    [JsonPropertyName("card_brand")]
    public required string CardBrand { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("card_expiration")]
    public string? CardExpiration { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("last_four")]
    public string? LastFour { get; init; }

    [JsonPropertyName("masked_card_number")]
    public required string MaskedCardNumber { get; init; }

    [JsonPropertyName("type")]
    public required InvoiceEventPaymentMethod Type { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
