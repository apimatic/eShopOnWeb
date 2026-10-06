using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing.Models;

/// <summary>
/// A customer record in Maxio Advanced Billing. The <see cref="Reference"/> is the unique
/// identifier from eShopOnWeb (the signed-in user's account name / email), which makes
/// customer provisioning idempotent per the spec's "one customer per reference" rule.
/// </summary>
public record BillingCustomer
{
    public long Id { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
}
