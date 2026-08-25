using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Buyer() { }

    public Buyer(string buyerId) : this()
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        BuyerId = buyerId;
        PayPalCustomerId = Guid.NewGuid().ToString("N");
    }

    /// <summary>The app's own buyer identifier (matches Order/Basket BuyerId - currently the signed-in username).</summary>
    public string BuyerId { get; private set; }

    /// <summary>A stable identifier this app assigns the shopper, sent to PayPal as the vault customer id.</summary>
    public string PayPalCustomerId { get; private set; }

    private readonly List<PaymentMethod> _paymentMethods = new();
    public IReadOnlyCollection<PaymentMethod> PaymentMethods => _paymentMethods.AsReadOnly();

    public PaymentMethod AddPaymentMethod(string vaultId, string cardBrand, string last4, string expiry, string? alias)
    {
        var paymentMethod = new PaymentMethod(vaultId, cardBrand, last4, expiry, alias);
        _paymentMethods.Add(paymentMethod);
        return paymentMethod;
    }

    public void RemovePaymentMethod(PaymentMethod paymentMethod)
    {
        _paymentMethods.Remove(paymentMethod);
    }
}
