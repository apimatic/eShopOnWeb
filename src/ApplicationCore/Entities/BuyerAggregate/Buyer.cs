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

    /// <summary>Saves a vaulted card for this shopper and returns the created payment method.</summary>
    public PaymentMethod AddPaymentMethod(string cardId, string? alias, string last4, string? cardBrand, int? expiryMonth, int? expiryYear)
    {
        var paymentMethod = new PaymentMethod(cardId, alias, last4, cardBrand, expiryMonth, expiryYear);
        _paymentMethods.Add(paymentMethod);
        return paymentMethod;
    }

    public PaymentMethod? FindPaymentMethod(int paymentMethodId) =>
        _paymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);

    /// <summary>Removes a saved card so it can no longer be seen or used to pay. Returns the removed method (for vault cleanup) or null.</summary>
    public PaymentMethod? RemovePaymentMethod(int paymentMethodId)
    {
        var paymentMethod = FindPaymentMethod(paymentMethodId);
        if (paymentMethod is not null)
        {
            _paymentMethods.Remove(paymentMethod);
        }
        return paymentMethod;
    }
}
