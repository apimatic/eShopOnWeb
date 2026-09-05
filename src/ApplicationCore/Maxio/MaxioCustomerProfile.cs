namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// The eShopOnWeb-side identity used to ensure a matching Maxio customer exists.
/// <see cref="Reference"/> is the stable key (the ApplicationUser id) used for idempotent
/// lookup: the same buyer always maps to the same Maxio customer, no matter how many times
/// this is submitted.
/// </summary>
public class MaxioCustomerProfile
{
    public required string Reference { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
}
