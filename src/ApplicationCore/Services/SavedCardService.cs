using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalClient _payPal;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalClient payPal,
        IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<int> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Reuse the shopper's existing PayPal customer id so all their cards vault under one customer.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        var existingCustomerId = existing.FirstOrDefault()?.PayPalCustomerId;

        var vaulted = await _payPal.VaultCardAsync(card, existingCustomerId, Guid.NewGuid().ToString("N"), cancellationToken);

        var saved = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.CustomerId, vaulted.Brand, vaulted.Last4, vaulted.Expiry);
        saved = await _repository.AddAsync(saved, cancellationToken);

        _logger.LogInformation("Saved card {0} for buyer {1} ({2}).", saved.Id, buyerId, saved.Descriptor);
        return saved.Id;
    }

    public async Task<IReadOnlyList<SavedCardView>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var cards = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        return cards
            .OrderByDescending(c => c.CreatedDate)
            .Select(c => new SavedCardView(c.Id, c.Brand, c.Last4, c.Expiry, c.Descriptor, c.CreatedDate))
            .ToList();
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var card = await _repository.GetByIdAsync(paymentMethodId, cancellationToken)
            ?? throw new PaymentEntityNotFoundException($"Saved card {paymentMethodId} was not found.");

        if (card.BuyerId != buyerId)
        {
            // Do not reveal another shopper's card.
            throw new PaymentEntityNotFoundException($"Saved card {paymentMethodId} was not found.");
        }

        // Remove from PayPal's vault first so it can no longer be charged, then locally.
        await _payPal.DeleteVaultedCardAsync(card.VaultId, cancellationToken);
        await _repository.DeleteAsync(card, cancellationToken);

        _logger.LogInformation("Deleted saved card {0} for buyer {1}.", paymentMethodId, buyerId);
    }
}
