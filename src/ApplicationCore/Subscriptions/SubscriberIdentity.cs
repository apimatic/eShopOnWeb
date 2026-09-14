namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user on whose behalf a billing operation runs. <see cref="Reference"/> is the
/// stable idempotency key mapped onto the Maxio customer <c>reference</c> field, so the same user
/// always resolves to the same Maxio customer (never two).
/// </summary>
/// <param name="Reference">Stable, deterministic key for the user (the login/username). Used as the Maxio customer reference.</param>
/// <param name="Email">User's email address, stored on the Maxio customer.</param>
/// <param name="FirstName">Best-effort first name for the Maxio customer (required by Maxio).</param>
/// <param name="LastName">Best-effort last name for the Maxio customer (required by Maxio).</param>
public record SubscriberIdentity(string Reference, string Email, string FirstName, string LastName);
