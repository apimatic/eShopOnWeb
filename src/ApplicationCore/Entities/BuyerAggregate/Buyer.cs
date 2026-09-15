using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    public string IdentityGuid { get; private set; }

    /// <summary>
    /// The PayPal-generated customer id that the shopper's vaulted cards are grouped under.
    /// Assigned the first time a card is vaulted, then reused for subsequent cards.
    /// </summary>
    public string? PayPalCustomerId { get; private set; }

    private List<PaymentMethod> _paymentMethods = new List<PaymentMethod>();

    public IEnumerable<PaymentMethod> PaymentMethods => _paymentMethods.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private Buyer() { }
#pragma warning restore CS8618

    public Buyer(string identity) : this()
    {
        Guard.Against.NullOrEmpty(identity, nameof(identity));
        IdentityGuid = identity;
    }

    public void SetPayPalCustomerId(string payPalCustomerId)
    {
        if (string.IsNullOrEmpty(PayPalCustomerId))
        {
            PayPalCustomerId = payPalCustomerId;
        }
    }

    public PaymentMethod AddPaymentMethod(PaymentMethod paymentMethod)
    {
        Guard.Against.Null(paymentMethod, nameof(paymentMethod));
        _paymentMethods.Add(paymentMethod);
        return paymentMethod;
    }

    public PaymentMethod? FindPaymentMethod(int paymentMethodId) =>
        _paymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);

    /// <summary>Removes a saved card. After this it can neither be listed nor used to pay.</summary>
    public bool RemovePaymentMethod(int paymentMethodId)
    {
        var existing = FindPaymentMethod(paymentMethodId);
        if (existing is null) return false;
        _paymentMethods.Remove(existing);
        return true;
    }
}
