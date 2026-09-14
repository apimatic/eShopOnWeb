namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb identity a billing customer is created for.
/// </summary>
/// <param name="UserId">The ASP.NET Identity user id. Carried for diagnostics only, see remarks.</param>
/// <param name="Email">E-mail address of the user; the stable key the provider customer reference is derived from.</param>
/// <param name="FirstName">Given name sent to the provider.</param>
/// <param name="LastName">Family name sent to the provider.</param>
/// <param name="Organization">Optional organisation name.</param>
/// <remarks>
/// The provider customer reference is derived from <paramref name="Email"/> rather than
/// <paramref name="UserId"/>. eShopOnWeb can be run against the in-memory identity store, which
/// mints fresh user ids on every start, whereas the seeded e-mail address is stable. Deriving the
/// reference from a value that survives a restart is what makes "ensure the customer exists"
/// genuinely idempotent rather than idempotent-until-the-next-restart.
/// </remarks>
public record SubscriberProfile(
    string UserId,
    string Email,
    string FirstName,
    string LastName,
    string? Organization = null);
