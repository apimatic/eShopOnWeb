using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A durable claim for a money-moving operation. The key is the primary key, so
/// a second caller inserting the same key is refused by the store before the
/// provider is called.
/// </summary>
public class PaymentOperationClaim
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentOperationClaim() {}

    public PaymentOperationClaim(string key, string operation)
    {
        Key = key;
        Operation = operation;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string Key { get; private set; }
    public string Operation { get; private set; }
    public string? ReferenceId { get; private set; }
    public string? ResultJson { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public bool IsCompleted => CompletedAt is not null;

    public void Complete(string? referenceId, string? resultJson)
    {
        ReferenceId = referenceId;
        ResultJson = resultJson;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
