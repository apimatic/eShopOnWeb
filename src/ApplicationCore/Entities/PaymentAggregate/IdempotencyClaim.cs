using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single-use claim written to the application's own store BEFORE a PayPal write, keyed by a deterministic
/// <see cref="Key"/>. Inserting a duplicate key fails on save (the store — including the EF in-memory
/// provider — enforces the primary key), which is how a double-click is rejected before it can reach PayPal.
/// The claim is released (deleted) if the provider refuses the call, so a genuine retry is still possible.
/// </summary>
public class IdempotencyClaim : IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private IdempotencyClaim() { }
#pragma warning restore CS8618

    public IdempotencyClaim(string key)
    {
        Guard.Against.NullOrEmpty(key, nameof(key));
        Key = key;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The primary key — the duplicate-insert conflict is the concurrency guard.</summary>
    public string Key { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
