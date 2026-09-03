using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Core.Validation;
using Maxio.Core.Validation.Attributes;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record PaymentMethodPaypal
{
    [JsonPropertyName("email")]
    [Format(FormatKind.Email)]
    public required string Email { get; init; }

    [JsonPropertyName("type")]
    public required InvoiceEventPaymentMethod Type { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
