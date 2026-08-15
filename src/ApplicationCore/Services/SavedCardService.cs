using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Saves, lists and removes a shopper's cards. Cards are vaulted at PayPal; only the vault token id
/// and safe display fields are kept locally, scoped to the buyer who saved them.
/// </summary>
public class SavedCardService : ISavedCardService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalClient _payPal;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<Buyer> buyerRepository, IPayPalClient payPal,
        IAppLogger<SavedCardService> logger)
    {
        _buyerRepository = buyerRepository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias,
        CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var (buyer, isNew) = await GetOrCreateBuyerAsync(buyerId, ct);

        VaultCardResult result;
        try
        {
            result = await _payPal.VaultCardAsync(card, buyer.PayPalCustomerId,
                requestId: $"vault-{buyerId}-{Guid.NewGuid():N}", ct);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException($"The card could not be saved: {ex.DescribeIssues()}", ex);
        }

        if (!string.IsNullOrEmpty(result.CustomerId))
        {
            buyer.SetPayPalCustomerId(result.CustomerId!);
        }
        var method = buyer.AddPaymentMethod(result.VaultTokenId, alias, result.Brand, result.Last4, result.Expiry);

        if (isNew)
        {
            await _buyerRepository.AddAsync(buyer, ct);
        }
        else
        {
            await _buyerRepository.UpdateAsync(buyer, ct);
        }

        _logger.LogInformation("Saved {0} card ending {1} for buyer {2} (token {3}).",
            result.Brand, result.Last4, buyerId, result.VaultTokenId);
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListCardsAsync(string buyerId, CancellationToken ct = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpec(buyerId), ct);
        return buyer?.PaymentMethods.ToList() ?? new List<PaymentMethod>();
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpec(buyerId), ct)
            ?? throw new EntityNotFoundException($"Saved card {paymentMethodId} was not found.");

        var method = buyer.FindPaymentMethod(paymentMethodId)
            ?? throw new EntityNotFoundException($"Saved card {paymentMethodId} was not found.");

        if (!string.IsNullOrEmpty(method.VaultTokenId))
        {
            try
            {
                await _payPal.DeleteVaultCardAsync(method.VaultTokenId!, ct);
            }
            catch (PayPalApiException ex) when (ex.StatusCode == 404)
            {
                // Already gone at PayPal; removing our record still makes it unusable, which is the goal.
                _logger.LogWarning("Vault token {0} for buyer {1} was already absent at PayPal.",
                    method.VaultTokenId, buyerId);
            }
        }

        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, ct);
        _logger.LogInformation("Removed saved card {0} for buyer {1}.", paymentMethodId, buyerId);
    }

    private async Task<(Buyer buyer, bool isNew)> GetOrCreateBuyerAsync(string buyerId, CancellationToken ct)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpec(buyerId), ct);
        if (buyer is not null)
        {
            return (buyer, false);
        }
        return (new Buyer(buyerId), true);
    }
}
