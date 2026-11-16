using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class IdempotencyClaim
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private IdempotencyClaim() { }

    public IdempotencyClaim(string claimKey, string operation, string? orderId = null)
    {
        Guard.Against.NullOrEmpty(claimKey, nameof(claimKey));
        Guard.Against.NullOrEmpty(operation, nameof(operation));

        ClaimKey = claimKey;
        Operation = operation;
        OrderId = orderId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string ClaimKey { get; private set; }
    public string Operation { get; private set; }
    public string? OrderId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
