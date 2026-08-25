using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    public string IdentityGuid { get; private set; }

    private readonly List<PaymentMethod> _paymentMethods = new();

    public IReadOnlyCollection<PaymentMethod> PaymentMethods => _paymentMethods.AsReadOnly();

    #pragma warning disable CS8618 // Required by Entity Framework
    private Buyer() { }

    public Buyer(string identity) : this()
    {
        Guard.Against.NullOrEmpty(identity, nameof(identity));
        IdentityGuid = identity;
    }

    public PaymentMethod AddPaymentMethod(string vaultId, string brand, string last4, string expiryYearMonth,
        string? alias, System.DateTimeOffset createdAt)
    {
        var paymentMethod = new PaymentMethod(Id, vaultId, brand, last4, expiryYearMonth, alias, createdAt);
        _paymentMethods.Add(paymentMethod);
        return paymentMethod;
    }

    public PaymentMethod GetPaymentMethod(int paymentMethodId)
    {
        var paymentMethod = _paymentMethods.FirstOrDefault(p => p.Id == paymentMethodId);
        if (paymentMethod is null)
            throw new PaymentMethodNotFoundException(paymentMethodId);
        return paymentMethod;
    }

    public void RemovePaymentMethod(int paymentMethodId)
    {
        var paymentMethod = GetPaymentMethod(paymentMethodId);
        _paymentMethods.Remove(paymentMethod);
    }
}
