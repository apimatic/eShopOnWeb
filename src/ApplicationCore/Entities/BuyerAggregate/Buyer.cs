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

    /// <summary>Adds a vaulted (saved) payment method to this buyer.</summary>
    public PaymentMethod AddPaymentMethod(string? alias, string vaultId, string? brand, string? last4, string? expiry)
    {
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        var paymentMethod = new PaymentMethod(Id, alias, vaultId, brand, last4, expiry);
        _paymentMethods.Add(paymentMethod);
        return paymentMethod;
    }

    /// <summary>Removes a saved payment method from this buyer. Returns true when it existed.</summary>
    public bool RemovePaymentMethod(int paymentMethodId)
    {
        var existing = _paymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);
        if (existing is null)
        {
            return false;
        }

        _paymentMethods.Remove(existing);
        return true;
    }

    public PaymentMethod? FindPaymentMethod(int paymentMethodId) => _paymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);
}
