namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A Maxio customer mapped to an eShopOnWeb user via <see cref="Reference"/>.
/// </summary>
public sealed record BillingCustomer(long Id, string? Reference, string Email);

public sealed record NewBillingCustomer(string Reference, string Email, string FirstName, string LastName);

public sealed record NewBillingSubscription(
    string ProductHandle,
    long CustomerId,
    string Reference,
    string UniquenessToken,
    string? PaymentCollectionMethod);
