using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Maxio;

/// <summary>
/// Persists the mapping between an eShopOnWeb user and their Maxio subscription so that
/// subscribing is idempotent.
/// </summary>
public class MaxioSubscriptionMapping : BaseEntity, IAggregateRoot
{
    public MaxioSubscriptionMapping(string applicationUserId, int maxioSubscriptionId, int maxioCustomerId, string productHandle)
    {
        ApplicationUserId = applicationUserId;
        MaxioSubscriptionId = maxioSubscriptionId;
        MaxioCustomerId = maxioCustomerId;
        ProductHandle = productHandle;
        CreatedAtUtc = DateTime.UtcNow;
    }

    private MaxioSubscriptionMapping()
    {
        // Required by EF Core
    }

    public string ApplicationUserId { get; private set; } = string.Empty;
    public int MaxioSubscriptionId { get; private set; }
    public int MaxioCustomerId { get; private set; }
    public string ProductHandle { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
}
