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

#pragma warning disable CS8618
    private Buyer() { }
#pragma warning restore CS8618

    public Buyer(string identity) : this()
    {
        Guard.Against.NullOrEmpty(identity, nameof(identity));
        IdentityGuid = identity;
    }

    public PaymentMethod AddPaymentMethod(string vaultToken, string? last4, string? cardBrand,
        string? expiryMonth, string? expiryYear, string? alias = null)
    {
        var method = new PaymentMethod(Id, vaultToken, last4, cardBrand, expiryMonth, expiryYear, alias);
        _paymentMethods.Add(method);
        return method;
    }

    public bool RemovePaymentMethod(int paymentMethodId)
    {
        var method = _paymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);
        if (method == null) return false;
        _paymentMethods.Remove(method);
        return true;
    }
}
