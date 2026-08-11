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

    /// <summary>Save a card (already vaulted at PayPal) for this shopper.</summary>
    public PaymentMethod AddPaymentMethod(string cardId, string brand, string last4, string? expiry, string? alias)
    {
        var method = new PaymentMethod(cardId, brand, last4, expiry, alias);
        _paymentMethods.Add(method);
        return method;
    }

    public PaymentMethod? FindPaymentMethod(int paymentMethodId) =>
        _paymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);

    /// <summary>
    /// Remove a saved card. Returns the removed card (so its vault token can be deleted at PayPal),
    /// or null if this shopper has no such card.
    /// </summary>
    public PaymentMethod? RemovePaymentMethod(int paymentMethodId)
    {
        var method = FindPaymentMethod(paymentMethodId);
        if (method is not null)
        {
            _paymentMethods.Remove(method);
        }
        return method;
    }
}
