namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

internal sealed class CreateCustomerRequest
{
    public CustomerAttributes Customer { get; set; } = new();
}

internal sealed class CustomerAttributes
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Organization { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

internal sealed class CreateSubscriptionRequest
{
    public CreateSubscriptionAttributes Subscription { get; set; } = new();

    public string UniquenessToken { get; set; } = string.Empty;
}

internal sealed class CreateSubscriptionAttributes
{
    public string ProductHandle { get; set; } = string.Empty;

    public string CustomerReference { get; set; } = string.Empty;

    /// <summary>
    /// No card is captured for these plans, so the subscription is created on remittance
    /// (invoice) collection instead of attempting an automatic charge without a payment profile.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}
