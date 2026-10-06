using System;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration.Entities;

/// <summary>
/// A sign-in the shop started. Only the SHA-256 of the state value is kept; the row is deleted when the
/// callback uses it, so every state is single-use.
/// </summary>
public class SquareOAuthState
{
    public string StateHash { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
