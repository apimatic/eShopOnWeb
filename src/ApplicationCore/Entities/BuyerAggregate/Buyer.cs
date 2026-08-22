using System.Collections.Generic;
using System.Linq;
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

    public void SetPayPalCustomerId(string payPalCustomerId)
    {
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));
        PayPalCustomerId = payPalCustomerId;
    }

    public PaymentMethod AddPaymentMethod(string vaultTokenId, string? last4, string? brand, string? expiry, string? alias)
    {
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));
        var method = new PaymentMethod(vaultTokenId, last4, brand, expiry, alias);
        _paymentMethods.Add(method);
        return method;
    }

    public PaymentMethod? FindPaymentMethod(int paymentMethodId)
    {
        return _paymentMethods.FirstOrDefault(m => m.Id == paymentMethodId);
    }

    public bool RemovePaymentMethod(int paymentMethodId)
    {
        var existing = FindPaymentMethod(paymentMethodId);
        if (existing is null)
        {
            return false;
        }

        _paymentMethods.Remove(existing);
        return true;
    }
}
