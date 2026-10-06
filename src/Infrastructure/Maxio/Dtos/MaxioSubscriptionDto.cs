namespace Microsoft.eShopWeb.Infrastructure.Maxio.Dtos;

/// <summary>Subscription resource as returned by the Maxio API.</summary>
public class MaxioSubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public long ProductPriceInCents { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public MaxioProductDto? Product { get; set; }
    public MaxioCustomerDto? Customer { get; set; }
}
