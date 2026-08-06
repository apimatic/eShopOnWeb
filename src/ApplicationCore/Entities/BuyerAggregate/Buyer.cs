using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    public string IdentityGuid { get; private set; }

    /// <summary>
    /// The PayPal-generated customer id under which this shopper's vaulted cards are grouped.
    /// Set the first time a card is saved; reused on subsequent saves so all of a shopper's
    /// cards belong to a single PayPal customer.
    /// </summary>
    public string? PayPalCustomerId { get; private set; }

    private List<PaymentMethod> _paymentMethods = new List<PaymentMethod>();

    public IEnumerable<PaymentMethod> PaymentMethods => _paymentMethods.AsReadOnly();

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

    public PaymentMethod AddPaymentMethod(PaymentMethod paymentMethod)
    {
        Guard.Against.Null(paymentMethod, nameof(paymentMethod));
        _paymentMethods.Add(paymentMethod);
        return paymentMethod;
    }

    /// <summary>Removes a saved card owned by this buyer. Returns the removed card, or null if not found.</summary>
    public PaymentMethod? RemovePaymentMethod(int paymentMethodId)
    {
        var existing = _paymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);
        if (existing is not null)
        {
            _paymentMethods.Remove(existing);
        }
        return existing;
    }
}
