using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Paypal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalPaymentGateway _payPal;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<Buyer> buyerRepository, IPayPalPaymentGateway payPal,
        IAppLogger<SavedCardService> logger)
    {
        _buyerRepository = buyerRepository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, SaveCardInput input, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(input, nameof(input));
        Guard.Against.Null(input.Card, nameof(input.Card));

        // Card details go straight to PayPal's vault; only the returned token + safe summary are kept.
        var vaulted = await _payPal.VaultCardAsync(input.Card, ct);

        var (buyer, isNew) = await GetOrCreateBuyerAsync(buyerId, ct);
        var method = buyer.AddPaymentMethod(vaulted.VaultId, input.Alias, vaulted.Brand, vaulted.Last4,
            vaulted.ExpiryMonth, vaulted.ExpiryYear);

        if (isNew) await _buyerRepository.AddAsync(buyer, ct);
        else await _buyerRepository.UpdateAsync(buyer, ct);

        _logger.LogInformation($"Saved card for {buyerId}: vault={vaulted.VaultId} {vaulted.Brand} ****{vaulted.Last4}.");
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
        return buyer?.PaymentMethods.ToList() ?? new List<PaymentMethod>();
    }

    public async Task RemoveCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
        var method = buyer?.FindPaymentMethod(paymentMethodId);
        if (buyer is null || method is null)
            throw new PaymentMethodNotFoundException(paymentMethodId);

        // Remove from PayPal's vault first (best-effort — a card already gone there is fine), then locally.
        await _payPal.DeleteVaultedCardAsync(method.PayPalVaultId, ct);
        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, ct);

        _logger.LogInformation($"Removed saved card {paymentMethodId} (vault={method.PayPalVaultId}) for {buyerId}.");
    }

    private async Task<(Buyer Buyer, bool IsNew)> GetOrCreateBuyerAsync(string buyerId, CancellationToken ct)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
        if (buyer is not null) return (buyer, false);
        return (new Buyer(buyerId), true);
    }
}
