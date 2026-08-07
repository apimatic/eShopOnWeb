using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPaymentGateway _paymentGateway;

    public PaymentMethodService(IRepository<Buyer> buyerRepository, IPaymentGateway paymentGateway)
    {
        _buyerRepository = buyerRepository;
        _paymentGateway = paymentGateway;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);

        // Saving the same card again returns the existing entry rather than creating a duplicate.
        var last4 = Last4(card.Number);
        var expiry = FormatExpiry(card);
        var existing = buyer?.PaymentMethods.FirstOrDefault(pm => pm.Last4 == last4 && pm.Expiry == expiry);
        if (existing is not null)
        {
            return existing;
        }

        // Vault the card at the provider — only a safe reference is ever stored locally. A fresh
        // request id per attempt keeps this from colliding with any earlier save; the single-send
        // handler protects the one call against network-retry duplicates.
        var vaulted = await _paymentGateway.VaultCardAsync(new VaultCardRequest(card, Guid.NewGuid().ToString()), cancellationToken);

        var isNewBuyer = buyer is null;
        buyer ??= new Buyer(buyerId);

        var paymentMethod = buyer.AddPaymentMethod(alias, vaulted.TokenId, vaulted.CardBrand, vaulted.Last4, vaulted.Expiry);

        if (isNewBuyer)
        {
            await _buyerRepository.AddAsync(buyer, cancellationToken);
        }
        else
        {
            await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        }

        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        return buyer?.PaymentMethods.ToList() ?? new List<PaymentMethod>();
    }

    public async Task<bool> DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        if (buyer is null)
        {
            return false;
        }

        var removed = buyer.RemovePaymentMethod(paymentMethodId);
        if (removed)
        {
            await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        }

        return removed;
    }

    private static string Last4(string number) =>
        number.Length >= 4 ? number[^4..] : number;

    // PayPal's card expiry format, matching what the vault returns.
    private static string FormatExpiry(CardDetails card) =>
        string.Format(CultureInfo.InvariantCulture, "{0:D4}-{1:D2}", card.ExpiryYear, card.ExpiryMonth);
}
