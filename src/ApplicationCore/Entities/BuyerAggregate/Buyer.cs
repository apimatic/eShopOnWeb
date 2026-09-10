using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Buyer : BaseEntity, IAggregateRoot
{
    /// <summary>The shopper's identity (their token name / email). A saved card
    /// belongs to the buyer who saved it.</summary>
    public string IdentityGuid { get; private set; }

    private List<PaymentMethod> _paymentMethods = new List<PaymentMethod>();

    public IEnumerable<PaymentMethod> PaymentMethods => _paymentMethods.AsReadOnly();

    #pragma warning disable CS8618 // Required by Entity Framework
    private Buyer() { }

    public Buyer(string identity) : this()
    {
        Guard.Against.NullOrEmpty(identity, nameof(identity));
        IdentityGuid = identity;
    }

    public PaymentMethod AddPaymentMethod(PaymentMethod paymentMethod)
    {
        Guard.Against.Null(paymentMethod, nameof(paymentMethod));
        _paymentMethods.Add(paymentMethod);
        return paymentMethod;
    }

    public PaymentMethod? FindPaymentMethod(int paymentMethodId) =>
        _paymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);

    /// <summary>Removes a saved card owned by this buyer. Returns the removed card,
    /// or null when this buyer has no such card.</summary>
    public PaymentMethod? RemovePaymentMethod(int paymentMethodId)
    {
        var pm = FindPaymentMethod(paymentMethodId);
        if (pm is not null)
        {
            _paymentMethods.Remove(pm);
        }
        return pm;
    }
}
