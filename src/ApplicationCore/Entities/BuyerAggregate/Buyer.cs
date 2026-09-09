using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    public string IdentityGuid { get; private set; }

    /// <summary>
    /// The PayPal customer id that groups this shopper's vaulted cards. Assigned by PayPal the first
    /// time a card is saved, and reused so all of the shopper's saved cards share one customer record.
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

    public PaymentMethod? FindPaymentMethod(int paymentMethodId) =>
        _paymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);

    /// <summary>Removes a saved card so it can no longer be listed or used to pay.</summary>
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
