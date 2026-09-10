using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    /// <summary>The shopper's identity (the authenticated username from the token).</summary>
    public string IdentityGuid { get; private set; }

    /// <summary>
    /// The PayPal-generated customer id under which this shopper's cards are vaulted. Set the first time the
    /// shopper saves a card, and reused so every card they save belongs to the same PayPal customer profile.
    /// </summary>
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
        if (string.IsNullOrEmpty(PayPalCustomerId))
        {
            PayPalCustomerId = payPalCustomerId;
        }
    }

    public PaymentMethod AddPaymentMethod(PaymentMethod paymentMethod)
    {
        _paymentMethods.Add(paymentMethod);
        return paymentMethod;
    }

    public PaymentMethod? FindPaymentMethod(int paymentMethodId) =>
        _paymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);

    /// <summary>Removes a saved card. Returns the removed card, or null if it was not found for this shopper.</summary>
    public PaymentMethod? RemovePaymentMethod(int paymentMethodId)
    {
        var method = FindPaymentMethod(paymentMethodId);
        if (method != null)
        {
            _paymentMethods.Remove(method);
        }
        return method;
    }
}
