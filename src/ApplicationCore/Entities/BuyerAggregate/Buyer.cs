using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    public string IdentityGuid { get; private set; }
    private readonly List<PaymentMethod> _paymentMethods = new();
    public IReadOnlyCollection<PaymentMethod> PaymentMethods => _paymentMethods.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private Buyer() { }
#pragma warning restore CS8618

    public Buyer(string identity)
    {
        Guard.Against.NullOrEmpty(identity, nameof(identity));
        IdentityGuid = identity;
    }

    public PaymentMethod AddPaymentMethod(string vaultId, string customerId, string brand,
        string last4, string expiry, string? alias)
    {
        var paymentMethod = new PaymentMethod(vaultId, customerId, brand, last4, expiry, alias);
        _paymentMethods.Add(paymentMethod);
        return paymentMethod;
    }

    public bool RemovePaymentMethod(int paymentMethodId)
    {
        var paymentMethod = _paymentMethods.SingleOrDefault(x => x.Id == paymentMethodId);
        return paymentMethod is not null && _paymentMethods.Remove(paymentMethod);
    }
}
