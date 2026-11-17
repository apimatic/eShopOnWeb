using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentOperationClaim
{
    private PaymentOperationClaim() { }

    public PaymentOperationClaim(string key)
    {
        Key = key;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string Key { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
