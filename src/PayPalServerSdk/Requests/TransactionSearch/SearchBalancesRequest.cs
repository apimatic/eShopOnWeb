using System.ComponentModel.DataAnnotations;

namespace PayPalServerSdk.Requests.TransactionSearch;

/// <summary>
/// The inputs of the SearchBalances operation.
/// </summary>
public sealed record SearchBalancesRequest
{
    /// <summary>
    /// List balances in the response at the date time provided, will return the last refreshed balance in the system when not provided.
    /// </summary>
    [StringLength(64, MinimumLength = 20)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[1-2][0-9]|3[0-1])[T,t]([0-1][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)([.][0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})$")]
    public string? AsOfTime { get; init; }

    /// <summary>
    /// Filters the transactions in the response by a <see href="https://developer.paypal.com/api/rest/reference/currency-codes/">three-character ISO-4217 currency code</see> for the PayPal transaction currency.
    /// </summary>
    [StringLength(3, MinimumLength = 3)]
    public string? CurrencyCode { get; init; }
}
