namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// Request body for <c>POST /subscriptions.json</c> (Create Subscription), per the
/// OpenAPI spec schema <c>Create-Subscription-Request</c>.
/// </summary>
public class CreateMaxioSubscriptionRequest
{
    public CreateMaxioSubscription Subscription { get; set; } = new();
}

/// <summary>
/// Subscription attributes for the Create Subscription operation, per the OpenAPI
/// spec schema <c>Create-Subscription</c>.
/// </summary>
public class CreateMaxioSubscription
{
    public string? ProductHandle { get; set; }
    public int? ProductId { get; set; }
    public string? ProductPricePointHandle { get; set; }
    public int? ProductPricePointId { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerReference { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public string? ReceivesInvoiceEmails { get; set; }
    public string? NetTerms { get; set; }
    public string? Currency { get; set; }
}
