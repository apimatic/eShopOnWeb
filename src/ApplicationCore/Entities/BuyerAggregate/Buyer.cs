using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    public string IdentityGuid { get; private set; }

    private List<PaymentMethod> _paymentMethods = new List<PaymentMethod>();

    public IReadOnlyCollection<PaymentMethod> PaymentMethods => _paymentMethods.AsReadOnly();

    #pragma warning disable CS8618 // Required by Entity Framework
    private Buyer() { }

    public Buyer(string identity) : this()
    {
        Guard.Against.NullOrEmpty(identity, nameof(identity));
        IdentityGuid = identity;
    }

    /// <summary>Saves a card (already vaulted with PayPal) for this shopper.</summary>
    public PaymentMethod AddPaymentMethod(string payPalVaultId, string? alias, string? brand, string? last4,
        int? expiryMonth, int? expiryYear)
    {
        var method = new PaymentMethod(payPalVaultId, alias, brand, last4, expiryMonth, expiryYear);
        _paymentMethods.Add(method);
        return method;
    }

    public PaymentMethod? FindPaymentMethod(int paymentMethodId) =>
        _paymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);

    /// <summary>Removes a saved card so it can no longer be seen or used to pay.</summary>
    public bool RemovePaymentMethod(int paymentMethodId)
    {
        var method = FindPaymentMethod(paymentMethodId);
        if (method is null) return false;
        _paymentMethods.Remove(method);
        return true;
    }
}
