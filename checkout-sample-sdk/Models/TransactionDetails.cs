using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The transaction details.
/// </summary>
public record TransactionDetails
{
    /// <summary>
    /// The transaction information.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("transaction_info")]
    public TransactionInformation? TransactionInfo { get; init; }

    /// <summary>
    /// The payer information.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payer_info")]
    public PayerInformation? PayerInfo { get; init; }

    /// <summary>
    /// The shipping information.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("shipping_info")]
    public ShippingInformation? ShippingInfo { get; init; }

    /// <summary>
    /// The cart information.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cart_info")]
    public CartInformation? CartInfo { get; init; }

    /// <summary>
    /// The store information.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("store_info")]
    public StoreInformation? StoreInfo { get; init; }

    /// <summary>
    /// The auction information.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("auction_info")]
    public AuctionInformation? AuctionInfo { get; init; }

    /// <summary>
    /// The incentive details.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("incentive_info")]
    public IncentiveInformation? IncentiveInfo { get; init; }
}
