using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalGateway _payPalGateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<Buyer> buyerRepository,
        IPayPalGateway payPalGateway,
        IAppLogger<SavedCardService> logger)
    {
        _buyerRepository = buyerRepository;
        _payPalGateway = payPalGateway;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, SaveCardInput input,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(input, nameof(input));
        Guard.Against.Null(input.Card, nameof(input.Card));

        var (buyer, isNew) = await GetOrCreateBuyerAsync(buyerId, cancellationToken);

        // Vault the card with PayPal. Group it under the buyer's PayPal customer id (assigned on
        // the first save, reused afterwards). Raw card details go to PayPal only, never persisted.
        var result = await _payPalGateway.VaultCardAsync(
            input.Card, buyer.PayPalCustomerId, Guid.NewGuid().ToString("N"), cancellationToken);

        if (string.IsNullOrEmpty(buyer.PayPalCustomerId) && !string.IsNullOrEmpty(result.CustomerId))
        {
            buyer.SetPayPalCustomerId(result.CustomerId!);
        }

        var paymentMethod = new PaymentMethod(result.VaultId, input.Alias, result.Brand, result.Last4, result.Expiry);
        buyer.AddPaymentMethod(paymentMethod);

        if (isNew)
        {
            await _buyerRepository.AddAsync(buyer, cancellationToken);
        }
        else
        {
            await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        }

        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListCardsAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(
            new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        return buyer is null
            ? Array.Empty<PaymentMethod>()
            : buyer.PaymentMethods.ToList();
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(
            new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        if (buyer is null)
        {
            return false;
        }

        var removed = buyer.RemovePaymentMethod(paymentMethodId);
        if (removed is null)
        {
            // Not the caller's card (or doesn't exist): report as not found.
            return false;
        }

        // Remove the token from PayPal so it can no longer be used to pay. If PayPal reports it is
        // already gone we still complete locally — the card is now removed and unusable here.
        if (!string.IsNullOrEmpty(removed.VaultId))
        {
            try
            {
                await _payPalGateway.DeleteVaultedCardAsync(removed.VaultId!, cancellationToken);
            }
            catch (PayPalException ex)
            {
                _logger.LogWarning($"Deleting PayPal vault token for payment method {paymentMethodId} failed: {ex.Message}");
            }
        }

        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        return true;
    }

    private async Task<(Buyer buyer, bool isNew)> GetOrCreateBuyerAsync(string buyerId, CancellationToken ct)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
        if (buyer is not null)
        {
            return (buyer, false);
        }
        return (new Buyer(buyerId), true);
    }
}
