using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// Details about the merchant cobranded card used for order purchase.
/// </summary>
public record CobrandedCard
{
    /// <summary>
    /// Array of labels for the cobranded card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("labels")]
    [MinLength(1)]
    [MaxLength(25)]
    public IReadOnlyList<string>? Labels { get; init; }

    /// <summary>
    /// The details for the merchant who receives the funds and fulfills the order. The merchant is also known as the payee.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payee")]
    public PayeeBase? Payee { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("amount")]
    public Money? Amount { get; init; }
}
