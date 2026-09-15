using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    public string IdentityGuid { get; private set; }

    /// <summary>
    /// PayPal customer id that groups this buyer's vaulted cards. Assigned by PayPal the
    /// first time a card is saved, then reused so later cards land under the same customer.
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

    public void SetPayPalCustomerId(string customerId)
    {
        Guard.Against.NullOrEmpty(customerId, nameof(customerId));
        PayPalCustomerId = customerId;
    }

    public PaymentMethod AddPaymentMethod(PaymentMethod paymentMethod)
    {
        Guard.Against.Null(paymentMethod, nameof(paymentMethod));
        _paymentMethods.Add(paymentMethod);
        return paymentMethod;
    }

    public PaymentMethod? FindPaymentMethod(int paymentMethodId) =>
        _paymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);

    /// <summary>
    /// Removes a saved card so it can no longer be seen or used to pay. Returns the removed
    /// card (so its vault token can be deleted from PayPal), or null if it wasn't the buyer's.
    /// </summary>
    public PaymentMethod? RemovePaymentMethod(int paymentMethodId)
    {
        var pm = FindPaymentMethod(paymentMethodId);
        if (pm is not null)
        {
            _paymentMethods.Remove(pm);
        }
        return pm;
    }
}
