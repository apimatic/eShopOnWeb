namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public decimal Price => PriceInCents / 100m;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public string? ProductFamilyName { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int ProductPriceInCents { get; set; }
    public decimal ProductPrice => ProductPriceInCents / 100m;
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public int CustomerId { get; set; }
    public int CurrentBillingAmountInCents { get; set; }
    public decimal CurrentBillingAmount => CurrentBillingAmountInCents / 100m;
    public string? Currency { get; set; }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class ListSubscriptionPlansRequest : BaseRequest
{
}

public class GetMySubscriptionsRequest : BaseRequest
{
}
