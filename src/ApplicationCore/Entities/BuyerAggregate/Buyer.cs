using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    public string IdentityGuid { get; private set; }
    public string PayPalCustomerId { get; private set; }

    private readonly List<PaymentMethod> _paymentMethods = new();

    public IReadOnlyCollection<PaymentMethod> PaymentMethods => _paymentMethods.AsReadOnly();

    #pragma warning disable CS8618 // Required by Entity Framework
    private Buyer() { }

    public Buyer(string identity) : this()
    {
        Guard.Against.NullOrEmpty(identity, nameof(identity));
        IdentityGuid = identity;
        PayPalCustomerId = CreatePayPalCustomerId(identity);
    }

    public PaymentMethod AddPaymentMethod(string vaultId, string last4, string? brand, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        var method = new PaymentMethod(vaultId, last4, brand, expiry, cardholderName);
        _paymentMethods.Add(method);
        return method;
    }

    public PaymentMethod GetPaymentMethod(int paymentMethodId)
    {
        var method = _paymentMethods.FirstOrDefault(m => m.Id == paymentMethodId);
        if (method is null)
        {
            throw new OrderPaymentException(404, "Saved card was not found.");
        }

        return method;
    }

    public PaymentMethod RemovePaymentMethod(int paymentMethodId)
    {
        var method = GetPaymentMethod(paymentMethodId);
        _paymentMethods.Remove(method);
        return method;
    }

    /// <summary>
    /// Stable PayPal customer id: 22-character identifier derived from the shopper identity,
    /// matching vault merchant_partner_customer_id (maxLength 22, [0-9a-zA-Z_-]).
    /// </summary>
    public static string CreatePayPalCustomerId(string identity)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash)[..22].ToLowerInvariant();
    }
}
