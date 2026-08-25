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
#pragma warning restore CS8618

    public Buyer(string identity) : this()
    {
        Guard.Against.NullOrEmpty(identity, nameof(identity));
        IdentityGuid = identity;
    }

    public PaymentMethod AddPaymentMethod(string vaultId, string? last4, string? brand, string? expiry)
    {
        var pm = new PaymentMethod(vaultId, last4, brand, expiry);
        _paymentMethods.Add(pm);
        return pm;
    }

    public void RemovePaymentMethod(int id)
    {
        var pm = _paymentMethods.FirstOrDefault(p => p.Id == id);
        if (pm != null)
            _paymentMethods.Remove(pm);
    }

    public PaymentMethod? FindPaymentMethod(int id) =>
        _paymentMethods.FirstOrDefault(p => p.Id == id);

    public PaymentMethod? FindPaymentMethodByVaultId(string vaultId) =>
        _paymentMethods.FirstOrDefault(p => p.CardId == vaultId);
}
