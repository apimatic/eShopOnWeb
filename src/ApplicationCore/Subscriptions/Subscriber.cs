namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb identity on whose behalf a billing operation is performed.
/// <para>
/// <see cref="ExternalId"/> is the stable key that ties an eShopOnWeb user to a customer in the
/// billing system. It has to survive application restarts, so it is derived from the caller's user
/// name - which is what the JWT carries - rather than from a database-generated identifier.
/// </para>
/// </summary>
public sealed record Subscriber(
    string ExternalId,
    string Email,
    string FirstName,
    string LastName,
    string? Organization = null);
