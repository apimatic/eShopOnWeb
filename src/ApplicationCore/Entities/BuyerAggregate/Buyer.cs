using System.Collections.Generic;
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

    public Buyer(string identity) : this()
    {
        Guard.Against.NullOrEmpty(identity, nameof(identity));
        IdentityGuid = identity;
    }

    public PaymentMethod AddPaymentMethod(string payPalVaultId, string brand,
        string lastDigits, string? expiry, System.DateTimeOffset createdAt)
    {
        var method = new PaymentMethod(payPalVaultId, brand, lastDigits, expiry, createdAt);
        _paymentMethods.Add(method);
        return method;
    }
}
