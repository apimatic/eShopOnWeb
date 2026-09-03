using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record PaymentMethodExternal
{
    [JsonPropertyName("details")]
    public required string? Details { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("memo")]
    public required string? Memo { get; init; }

    [JsonPropertyName("type")]
    public required InvoiceEventPaymentMethod Type { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
