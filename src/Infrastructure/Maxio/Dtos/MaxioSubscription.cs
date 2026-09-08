using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Dtos;

public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("balance_in_cents")]
    public long BalanceInCents { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }

    [JsonPropertyName("cancel_at_end_of_period")]
    public bool CancelAtEndOfPeriod { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public System.DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public System.DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public System.DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("created_at")]
    public System.DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public System.DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("activated_at")]
    public System.DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public System.DateTimeOffset? CanceledAt { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}
