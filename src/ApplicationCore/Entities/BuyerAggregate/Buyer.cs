using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    public string IdentityGuid { get; private set; }

    private List<PaymentMethod> _paymentMethods = new List<PaymentMethod>();

    public IEnumerable<PaymentMethod> PaymentMethods => _paymentMethods.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private Buyer() { }

    public Buyer(string identity) : this()
    {
        Guard.Against.NullOrEmpty(identity, nameof(identity));
        IdentityGuid = identity;
    }

    /// <summary>Save a card for reuse and return the created payment method.</summary>
    public PaymentMethod AddPaymentMethod(PaymentMethod paymentMethod)
    {
        Guard.Against.Null(paymentMethod, nameof(paymentMethod));
        _paymentMethods.Add(paymentMethod);
        return paymentMethod;
    }

    public PaymentMethod? FindPaymentMethod(int paymentMethodId)
        => _paymentMethods.FirstOrDefault(p => p.Id == paymentMethodId);

    /// <summary>Remove a saved card. Returns false if the card does not belong to this buyer.</summary>
    public bool RemovePaymentMethod(int paymentMethodId)
    {
        var existing = FindPaymentMethod(paymentMethodId);
        if (existing is null) return false;
        _paymentMethods.Remove(existing);
        return true;
    }
}
