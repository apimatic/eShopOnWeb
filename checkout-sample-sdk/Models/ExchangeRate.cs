using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The exchange rate that determines the amount to convert from one currency to another currency.
/// </summary>
public record ExchangeRate
{
    /// <summary>
    /// The <see href="/api/rest/reference/currency-codes/">three-character ISO-4217 currency code</see> that identifies the currency.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("source_currency")]
    [StringLength(3, MinimumLength = 3)]
    public string? SourceCurrency { get; init; }

    /// <summary>
    /// The <see href="/api/rest/reference/currency-codes/">three-character ISO-4217 currency code</see> that identifies the currency.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("target_currency")]
    [StringLength(3, MinimumLength = 3)]
    public string? TargetCurrency { get; init; }

    /// <summary>
    /// The target currency amount. Equivalent to one unit of the source currency. Formatted as integer or decimal value with one to 15 digits to the right of the decimal point.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("value")]
    public string? Value { get; init; }
}
