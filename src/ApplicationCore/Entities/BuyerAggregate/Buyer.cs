using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    public string IdentityGuid { get; private set; }

    /// <summary>The PayPal customer id under which this buyer's vaulted cards are grouped. Assigned by
    /// PayPal the first time a card is saved and reused for subsequent cards.</summary>
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

    /// <summary>Records the PayPal customer id once, so all of this buyer's saved cards share it.</summary>
    public void SetPayPalCustomerId(string payPalCustomerId)
    {
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));
        if (string.IsNullOrEmpty(PayPalCustomerId))
        {
            PayPalCustomerId = payPalCustomerId;
        }
    }

    /// <summary>Adds a saved card to this buyer and returns it.</summary>
    public PaymentMethod AddPaymentMethod(string vaultId, string brand, string last4, string expiry, string? alias)
    {
        var paymentMethod = new PaymentMethod(vaultId, brand, last4, expiry, alias);
        _paymentMethods.Add(paymentMethod);
        return paymentMethod;
    }

    /// <summary>Finds one of this buyer's saved cards by its id, or null if it isn't theirs.</summary>
    public PaymentMethod? FindPaymentMethod(int paymentMethodId)
        => _paymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);

    /// <summary>Removes a saved card. Returns the removed card, or null if it wasn't theirs.</summary>
    public PaymentMethod? RemovePaymentMethod(int paymentMethodId)
    {
        var paymentMethod = FindPaymentMethod(paymentMethodId);
        if (paymentMethod != null)
        {
            _paymentMethods.Remove(paymentMethod);
        }
        return paymentMethod;
    }
}
