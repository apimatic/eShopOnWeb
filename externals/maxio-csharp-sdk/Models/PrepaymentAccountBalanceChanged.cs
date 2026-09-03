using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record PrepaymentAccountBalanceChanged
{
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("prepayment_account_balance_in_cents")]
    public required long PrepaymentAccountBalanceInCents { get; init; }

    [JsonPropertyName("prepayment_balance_change_in_cents")]
    public required long PrepaymentBalanceChangeInCents { get; init; }

    [JsonPropertyName("currency_code")]
    public required string CurrencyCode { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
