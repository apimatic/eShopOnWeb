using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    public string IdentityGuid { get; private set; }
    public string? PayPalCustomerId { get; private set; }

    private List<PaymentMethod> _paymentMethods = new List<PaymentMethod>();

    public IReadOnlyCollection<PaymentMethod> PaymentMethods => _paymentMethods.AsReadOnly();

    #pragma warning disable CS8618 // Required by Entity Framework
    private Buyer() { }

    public Buyer(string identity) : this()
    {
        Guard.Against.NullOrEmpty(identity, nameof(identity));
        IdentityGuid = identity;
    }

    public void SetPayPalCustomerId(string customerId)
    {
        Guard.Against.NullOrEmpty(customerId, nameof(customerId));
        PayPalCustomerId = customerId;
    }

    public PaymentMethod AddPaymentMethod(string cardId, string? last4, string? brand, string? expiry, string? name)
    {
        Guard.Against.NullOrEmpty(cardId, nameof(cardId));
        var existing = _paymentMethods.FirstOrDefault(p => p.CardId == cardId);
        if (existing is not null)
        {
            existing.UpdateDisplay(last4, brand, expiry, name);
            return existing;
        }

        var method = new PaymentMethod(cardId, last4, brand, expiry, name);
        _paymentMethods.Add(method);
        return method;
    }

    public PaymentMethod? RemovePaymentMethod(string cardId)
    {
        var method = _paymentMethods.FirstOrDefault(p => p.CardId == cardId);
        if (method is null) return null;
        _paymentMethods.Remove(method);
        return method;
    }

    public PaymentMethod? FindPaymentMethod(string cardId) =>
        _paymentMethods.FirstOrDefault(p => p.CardId == cardId);
}
