using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    public string IdentityGuid { get; private set; }

    /// <summary>
    /// The PayPal Vault customer this buyer maps to. Captured the first time a card is saved so all
    /// of the buyer's cards are grouped under one PayPal customer on subsequent saves.
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

    /// <summary>
    /// Removes a saved card owned by this buyer. Returns the removed card (so its vault token can be
    /// deleted from PayPal), or null if this buyer has no such card.
    /// </summary>
    public PaymentMethod? RemovePaymentMethod(int paymentMethodId)
    {
        var paymentMethod = _paymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);
        if (paymentMethod is not null)
        {
            _paymentMethods.Remove(paymentMethod);
        }
        return paymentMethod;
    }
}
