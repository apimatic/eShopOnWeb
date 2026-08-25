using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Buyer() { }

    public Buyer(string buyerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        BuyerId = buyerId;
    }

    public string BuyerId { get; private set; }

    /// <summary>
    /// The PayPal-generated customer id returned the first time a card is vaulted for this buyer;
    /// reused thereafter to list their saved cards.
    /// </summary>
    public string? PayPalCustomerId { get; private set; }

    private readonly List<PaymentMethod> _paymentMethods = new();
    public IReadOnlyCollection<PaymentMethod> PaymentMethods => _paymentMethods.AsReadOnly();

    public void SetPayPalCustomerId(string payPalCustomerId)
    {
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));
        PayPalCustomerId = payPalCustomerId;
    }

    public PaymentMethod AddPaymentMethod(string payPalVaultId, string? brand, string lastDigits, string expiry, string? cardholderName)
    {
        var paymentMethod = new PaymentMethod(payPalVaultId, brand, lastDigits, expiry, cardholderName);
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
