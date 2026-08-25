using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The net amount. Returned when the currency of the refund is different from the currency of the PayPal account where the merchant holds their funds.
/// </summary>
public record NetAmountBreakdownItem
{
    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payable_amount")]
    public Money? PayableAmount { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("converted_amount")]
    public Money? ConvertedAmount { get; init; }

    /// <summary>
    /// The exchange rate that determines the amount to convert from one currency to another currency.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("exchange_rate")]
    public ExchangeRate? ExchangeRate { get; init; }
}
