using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    public string IdentityGuid { get; private set; }
    public string? PayPalCustomerId { get; private set; }

    private readonly List<PaymentMethod> _paymentMethods = new();
    public IEnumerable<PaymentMethod> PaymentMethods => _paymentMethods.AsReadOnly();

    #pragma warning disable CS8618 // Required by Entity Framework
    private Buyer() { }

    public Buyer(string identity) : this()
    {
        Guard.Against.NullOrEmpty(identity, nameof(identity));
        IdentityGuid = identity;
    }

    public PaymentMethod BeginAddingPaymentMethod(string requestId)
    {
        var method = new PaymentMethod(requestId);
        _paymentMethods.Add(method);
        return method;
    }

    public void CompleteAddingPaymentMethod(
        PaymentMethod method,
        string payPalPaymentTokenId,
        string? payPalCustomerId,
        string brand,
        string last4,
        string expiry,
        string? cardholderName)
    {
        if (!_paymentMethods.Contains(method))
        {
            throw new InvalidOperationException("The payment method does not belong to this buyer.");
        }

        PayPalCustomerId = payPalCustomerId ?? PayPalCustomerId;
        method.Complete(payPalPaymentTokenId, brand, last4, expiry, cardholderName);
    }

    public PaymentMethod? FindActivePaymentMethod(int id) =>
        _paymentMethods.SingleOrDefault(x => x.Id == id && x.IsActive);

    public void RemovePaymentMethod(PaymentMethod method)
    {
        if (!_paymentMethods.Contains(method))
        {
            throw new InvalidOperationException("The payment method does not belong to this buyer.");
        }

        method.Remove();
    }
}
