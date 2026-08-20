namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record CreateBillingSubscription(
    string ProductHandle,
    int CustomerId,
    string Reference,
    string UniquenessToken,
    string PaymentCollectionMethod = "remittance");
