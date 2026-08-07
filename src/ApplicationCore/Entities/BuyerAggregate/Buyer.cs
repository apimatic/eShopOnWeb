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

    /// <summary>Saves a card (already tokenised by the vault) for this buyer and returns it.</summary>
    public PaymentMethod AddPaymentMethod(string alias, string cardId, string last4, string? brand, string? expiryMonthYear)
    {
        var paymentMethod = new PaymentMethod(alias, cardId, last4, brand, expiryMonthYear);
        _paymentMethods.Add(paymentMethod);
        return paymentMethod;
    }

    /// <summary>Removes a saved card owned by this buyer. Returns the removed card, or null if not found.</summary>
    public PaymentMethod? RemovePaymentMethod(int paymentMethodId)
    {
        var paymentMethod = _paymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);
        if (paymentMethod is not null)
        {
            _paymentMethods.Remove(paymentMethod);
        }
        return paymentMethod;
    }

    public PaymentMethod? FindPaymentMethod(int paymentMethodId)
        => _paymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);
}
