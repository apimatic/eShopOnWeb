namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The identity attributes of the logged-in shopper that the subscription integration needs.
/// <see cref="Id"/> is the local user id and doubles as the unique Maxio customer reference, so
/// a customer can be found idempotently (find-before-create) even when the local cache is cold.
/// </summary>
public sealed record MaxioShopper(string Id, string Email, string? FirstName = null, string? LastName = null);
