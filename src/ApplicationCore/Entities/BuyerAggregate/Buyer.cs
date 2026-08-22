using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    public string IdentityGuid { get; private set; }
    public string PayPalCustomerId { get; private set; }

    private readonly List<PaymentMethod> _paymentMethods = new List<PaymentMethod>();

    public IReadOnlyCollection<PaymentMethod> PaymentMethods => _paymentMethods.AsReadOnly();

    #pragma warning disable CS8618 // Required by Entity Framework
    private Buyer() { }

    public Buyer(string identity) : this()
    {
        Guard.Against.NullOrEmpty(identity, nameof(identity));
        IdentityGuid = identity;
        PayPalCustomerId = BuildPayPalCustomerId(identity);
    }

    public PaymentMethod AddPaymentMethod(
        string paypalVaultId,
        string? last4,
        string? brand,
        string? expiry,
        string? alias)
    {
        Guard.Against.NullOrEmpty(paypalVaultId, nameof(paypalVaultId));
        var method = new PaymentMethod(paypalVaultId, last4, brand, expiry, alias);
        _paymentMethods.Add(method);
        return method;
    }

    public PaymentMethod? GetPaymentMethod(int paymentMethodId)
    {
        return _paymentMethods.FirstOrDefault(m => m.Id == paymentMethodId);
    }

    public bool RemovePaymentMethod(int paymentMethodId)
    {
        var method = GetPaymentMethod(paymentMethodId);
        if (method is null)
        {
            return false;
        }

        _paymentMethods.Remove(method);
        return true;
    }

    private static string BuildPayPalCustomerId(string identity)
    {
        // Vault list customer_id is ^[0-9a-zA-Z_-]+$ with max 36; keep a stable sanitized id.
        var sanitized = new string(identity
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_')
            .ToArray());

        if (sanitized.Length < 7)
        {
            sanitized = sanitized.PadRight(7, '0');
        }

        return sanitized.Length <= 36 ? sanitized : sanitized[..36];
    }
}
