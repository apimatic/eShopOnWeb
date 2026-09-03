using System;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CreditAccountBalanceChanged
{
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("service_credit_account_balance_in_cents")]
    public required long ServiceCreditAccountBalanceInCents { get; init; }

    [JsonPropertyName("service_credit_balance_change_in_cents")]
    public required long ServiceCreditBalanceChangeInCents { get; init; }

    [JsonPropertyName("currency_code")]
    public required string CurrencyCode { get; init; }

    [JsonPropertyName("at_time")]
    public required DateTimeOffset AtTime { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
