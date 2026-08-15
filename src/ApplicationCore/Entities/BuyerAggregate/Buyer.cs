using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    public string IdentityGuid { get; private set; }

    /// <summary>The PayPal customer id that groups this shopper's vaulted cards. Assigned by PayPal
    /// when the first card is saved and reused for subsequent cards.</summary>
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
        // Belongs to the buyer for life; do not overwrite once assigned.
        PayPalCustomerId ??= payPalCustomerId;
    }

    public PaymentMethod AddPaymentMethod(string vaultTokenId, string? alias, string? brand, string? last4, string? expiry)
    {
        var method = new PaymentMethod(vaultTokenId, alias, brand, last4, expiry);
        _paymentMethods.Add(method);
        return method;
    }

    public PaymentMethod? FindPaymentMethod(int paymentMethodId) =>
        _paymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);

    public bool RemovePaymentMethod(int paymentMethodId)
    {
        var method = FindPaymentMethod(paymentMethodId);
        if (method is null) return false;
        _paymentMethods.Remove(method);
        return true;
    }
}
