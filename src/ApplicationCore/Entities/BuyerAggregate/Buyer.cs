using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    public string IdentityGuid { get; private set; }
    public string? PaypalCustomerId { get; private set; }

    private readonly List<PaymentMethod> _paymentMethods = new List<PaymentMethod>();

    public IReadOnlyCollection<PaymentMethod> PaymentMethods => _paymentMethods.AsReadOnly();

    #pragma warning disable CS8618 // Required by Entity Framework
    private Buyer() { }

    public Buyer(string identity) : this()
    {
        Guard.Against.NullOrEmpty(identity, nameof(identity));
        IdentityGuid = identity;
    }

    public void SetPaypalCustomerId(string paypalCustomerId)
    {
        Guard.Against.NullOrEmpty(paypalCustomerId, nameof(paypalCustomerId));
        PaypalCustomerId = paypalCustomerId;
    }

    public PaymentMethod AddPaymentMethod(string alias, string vaultId, string? last4, string? brand, string? expiry)
    {
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        var method = new PaymentMethod(alias, vaultId, last4, brand, expiry);
        _paymentMethods.Add(method);
        return method;
    }

    public PaymentMethod RemovePaymentMethod(int paymentMethodId)
    {
        var method = _paymentMethods.FirstOrDefault(m => m.Id == paymentMethodId);
        if (method is null)
        {
            throw new PaymentNotFoundException($"Payment method {paymentMethodId} was not found.");
        }

        _paymentMethods.Remove(method);
        return method;
    }
}
