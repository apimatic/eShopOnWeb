using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    /// <summary>
    /// Identifies the shopper who owns this buyer record. Orders in this app are keyed by the
    /// authenticated user name (which is the email), so buyers use the same identity string to keep
    /// a single, consistent notion of "who the shopper is" across orders and saved cards.
    /// </summary>
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
    /// Saves a card (already tokenised in PayPal's vault) against this buyer and returns it.
    /// </summary>
    public PaymentMethod AddPaymentMethod(string vaultId, string cardBrand, string last4, string expiry, string? alias)
    {
        var paymentMethod = new PaymentMethod(vaultId, cardBrand, last4, expiry, alias);
        _paymentMethods.Add(paymentMethod);
        return paymentMethod;
    }

    /// <summary>
    /// Finds one of this buyer's saved cards by its id, or null if it does not belong to this buyer.
    /// </summary>
    public PaymentMethod? GetPaymentMethod(int paymentMethodId) =>
        _paymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);

    /// <summary>
    /// Removes a saved card from this buyer. Returns true if the card was found and removed.
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
