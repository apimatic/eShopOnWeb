using System;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration.Entities;

/// <summary>
/// The Square merchant this shop acts for after the merchant signed in through OAuth.
/// There is at most one row (<see cref="SingletonId"/>). Tokens are stored encrypted.
/// </summary>
public class SquareConnection
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public string MerchantId { get; set; } = string.Empty;
    public string? BusinessName { get; set; }
    public string ProtectedAccessToken { get; set; } = string.Empty;
    public string? ProtectedRefreshToken { get; set; }
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }
    public DateTimeOffset ConnectedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
