using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    public string IdentityGuid { get; private set; }

    private readonly List<PaymentMethod> _paymentMethods = new();

    public IEnumerable<PaymentMethod> PaymentMethods => _paymentMethods.AsReadOnly();

    #pragma warning disable CS8618 // Required by Entity Framework
    private Buyer() { }

    public Buyer(string identity) : this()
    {
        Guard.Against.NullOrEmpty(identity, nameof(identity));
        IdentityGuid = identity;
    }

    public PaymentMethod AddPaymentMethod(string alias, string payPalVaultId, string brand, string last4, string expiry)
    {
        var method = new PaymentMethod(alias, payPalVaultId, brand, last4, expiry);
        _paymentMethods.Add(method);
        return method;
    }

    public PaymentMethod? FindPaymentMethod(int id) => _paymentMethods.Find(x => x.Id == id);

    public void RemovePaymentMethod(PaymentMethod paymentMethod) => _paymentMethods.Remove(paymentMethod);
}
