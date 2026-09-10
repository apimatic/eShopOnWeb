using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The options that the payee or merchant offers to the payer to ship or pick up their items.
/// </summary>
public record ShippingOption
{
    /// <summary>
    /// A unique ID that identifies a payer-selected shipping option.
    /// </summary>
    [JsonPropertyName("id")]
    [MaxLength(127)]
    public required string Id { get; init; }

    /// <summary>
    /// A description that the payer sees, which helps them choose an appropriate shipping option. For example, <c>Free Shipping</c>, <c>USPS Priority Shipping</c>, <c>Expédition prioritaire USPS</c>, or <c>USPS yōuxiān fā huò</c>. Localize this description to the payer's locale.
    /// </summary>
    [JsonPropertyName("label")]
    [MaxLength(127)]
    public required string Label { get; init; }

    /// <summary>
    /// A classification for the method of purchase fulfillment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("type")]
    public ShippingType? Type { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("amount")]
    public Money? Amount { get; init; }

    /// <summary>
    /// If the API request sets <c>selected = true</c>, it represents the shipping option that the payee or merchant expects to be pre-selected for the payer when they first view the <c>shipping.options</c> in the PayPal Checkout experience. As part of the response if a <c>shipping.option</c> contains <c>selected=true</c>, it represents the shipping option that the payer selected during the course of checkout with PayPal. Only one <c>shipping.option</c> can be set to <c>selected=true</c>.
    /// </summary>
    [JsonPropertyName("selected")]
    public required bool Selected { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
