namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// The eShopOnWeb identity fields needed to ensure a matching Maxio customer exists.
/// <see cref="Reference"/> is the eShopOnWeb user id and is used as the Maxio customer's
/// "reference" (Maxio's supported idempotency key for customer lookup/creation).
/// </summary>
public record MaxioCustomerProfile(
    string Reference,
    string Email,
    string FirstName,
    string LastName);
