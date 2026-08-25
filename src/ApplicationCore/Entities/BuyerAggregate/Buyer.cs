using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    public string IdentityGuid { get; private set; }

    private readonly List<PaymentMethod> _paymentMethods = new List<PaymentMethod>();

    public IReadOnlyCollection<PaymentMethod> PaymentMethods => _paymentMethods.AsReadOnly();

    #pragma warning disable CS8618 // Required by Entity Framework
    private Buyer() { }

    public Buyer(string identity) : this()
    {
        Guard.Against.NullOrEmpty(identity, nameof(identity));
        IdentityGuid = identity;
    }

    public PaymentMethod AddPaymentMethod(string payPalVaultId, string? alias, string? brand, string? last4, string? expiry, string? cardType)
    {
        var paymentMethod = new PaymentMethod(Id, payPalVaultId, alias, brand, last4, expiry, cardType);
        _paymentMethods.Add(paymentMethod);
        return paymentMethod;
    }

    public bool RemovePaymentMethod(int paymentMethodId)
    {
        var paymentMethod = _paymentMethods.FirstOrDefault(p => p.Id == paymentMethodId);
        if (paymentMethod is null)
        {
            return false;
        }

        _paymentMethods.Remove(paymentMethod);
        return true;
    }
}
