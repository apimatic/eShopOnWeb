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

    /// <summary>
    /// Saves a card (already vaulted at PayPal) for this buyer and returns the created entry.
    /// </summary>
    public PaymentMethod AddPaymentMethod(string? alias, string payPalTokenId, string? cardBrand, string? last4, string? expiry)
    {
        var paymentMethod = new PaymentMethod(alias, payPalTokenId, cardBrand, last4, expiry);
        _paymentMethods.Add(paymentMethod);
        return paymentMethod;
    }

    /// <summary>
    /// Returns the buyer's saved card with the given id, or null if it is not theirs. Scoping
    /// lookups through the buyer aggregate is what guarantees one shopper cannot touch another's cards.
    /// </summary>
    public PaymentMethod? GetPaymentMethod(int paymentMethodId) =>
        _paymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);

    /// <summary>
    /// Removes a saved card. Returns true if a card was removed. After removal the card no longer
    /// appears among the buyer's cards and can no longer be used to pay.
    /// </summary>
    public bool RemovePaymentMethod(int paymentMethodId)
    {
        var paymentMethod = GetPaymentMethod(paymentMethodId);
        if (paymentMethod is null)
        {
            return false;
        }

        _paymentMethods.Remove(paymentMethod);
        return true;
    }
}
