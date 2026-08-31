using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentMethod> _paymentMethods = new();
    private Buyer() { }
    public Buyer(string identity) { Guard.Against.NullOrEmpty(identity, nameof(identity)); IdentityGuid = identity; }
    public string IdentityGuid { get; private set; } = string.Empty;
    public string? PayPalCustomerId { get; private set; }
    public IEnumerable<PaymentMethod> PaymentMethods => _paymentMethods.AsReadOnly();
    public void SetPayPalCustomerId(string customerId) => PayPalCustomerId = customerId;
    public PaymentMethod AddPaymentMethod(string token, string brand, string last4, string expiry, string? name)
    { var method = new PaymentMethod(token, brand, last4, expiry, name); _paymentMethods.Add(method); return method; }
    public void RemovePaymentMethod(PaymentMethod method, DateTimeOffset at)
    { if (!_paymentMethods.Contains(method)) throw new InvalidOperationException("The payment method does not belong to this buyer."); method.Remove(at); }
}
