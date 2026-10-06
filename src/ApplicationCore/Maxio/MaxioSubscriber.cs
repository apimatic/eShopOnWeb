namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Identifies the eShopOnWeb user who is subscribing. The Maxio customer reference is derived
/// from <see cref="UserId"/> so that it is stable and unique per user.
/// </summary>
public record MaxioSubscriber(string UserId, string UserName, string Email);
