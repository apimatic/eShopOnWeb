using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A durable, atomically-inserted claim on a mutating payment operation. Its primary key is the claim
/// <see cref="Key"/>; inserting a second row with the same key fails the store's primary-key uniqueness
/// check at save time, which is what rejects a concurrent duplicate (a double-click) — not an in-process
/// lock and not a read-before-write. A provider idempotency key (PayPal-Request-Id) is used alongside it
/// so the money movement is deduplicated at PayPal even if two requests race to the network.
/// </summary>
public class IdempotencyClaim : IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private IdempotencyClaim() { }

    public IdempotencyClaim(string key)
    {
        Key = key;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The primary key. Uniqueness of this value is what rejects a duplicate request.</summary>
    public string Key { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
