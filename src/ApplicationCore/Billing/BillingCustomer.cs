namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A Maxio customer that stands for an eShopOnWeb account in the billing system of record.
/// </summary>
/// <param name="Id">Maxio generated customer id.</param>
/// <param name="Reference">The eShopOnWeb supplied reference (unique per site).</param>
/// <param name="FirstName">First name on the billing record.</param>
/// <param name="LastName">Last name on the billing record.</param>
/// <param name="Email">Email on the billing record.</param>
/// <param name="NewlyCreated">False when the customer already existed and was reused (idempotent replay).</param>
public record BillingCustomer(
    long Id,
    string Reference,
    string? FirstName,
    string? LastName,
    string? Email,
    bool NewlyCreated);
