using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    public string IdentityGuid { get; private set; }
    public string? PayPalCustomerId { get; private set; }

    private readonly List<PaymentMethod> _paymentMethods = new List<PaymentMethod>();

    public IReadOnlyCollection<PaymentMethod> PaymentMethods => _paymentMethods.AsReadOnly();

    #pragma warning disable CS8618 // Required by Entity Framework
    private Buyer() { }

    public Buyer(string identity) : this()
    {
        Guard.Against.NullOrEmpty(identity, nameof(identity));
        IdentityGuid = identity;
    }

    public void SetPayPalCustomerId(string? payPalCustomerId)
    {
        if (!string.IsNullOrWhiteSpace(payPalCustomerId))
        {
            PayPalCustomerId = payPalCustomerId;
        }
    }

    public PaymentMethod AddPaymentMethod(string vaultTokenId, string? last4, string? brand, string? expiry, string? name)
    {
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));
        var method = new PaymentMethod(vaultTokenId, last4, brand, expiry, name);
        _paymentMethods.Add(method);
        return method;
    }

    public PaymentMethod? RemovePaymentMethod(int paymentMethodId)
    {
        var existing = _paymentMethods.Find(m => m.Id == paymentMethodId);
        if (existing == null)
        {
            return null;
        }

        _paymentMethods.Remove(existing);
        return existing;
    }

    public PaymentMethod? FindPaymentMethod(int paymentMethodId) =>
        _paymentMethods.Find(m => m.Id == paymentMethodId);
}
