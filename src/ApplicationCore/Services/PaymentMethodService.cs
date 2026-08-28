using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Saving, listing and removing a shopper's cards. The card itself goes straight to the processor's
/// vault; what is kept here is the vault reference plus a safe description.
/// </summary>
public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedCard> savedCardRepository,
        IPaymentGateway gateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        ValidateCard(card);

        // Keep all of a shopper's cards under the one processor-side customer, so the vault groups
        // them the way the shopper does. The first card creates that customer.
        var existing = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
        var customerId = existing.Select(c => c.PayPalCustomerId).FirstOrDefault(id => !string.IsNullOrEmpty(id));

        var vaulted = await _gateway.VaultCardAsync(
            card,
            customerId,
            $"eshop-pm-{Guid.NewGuid():N}",
            cancellationToken);

        var savedCard = new SavedCard(
            buyerId,
            vaulted.VaultId,
            vaulted.CustomerId ?? customerId,
            vaulted.Brand,
            vaulted.LastDigits,
            vaulted.Expiry,
            vaulted.CardholderName ?? card.CardholderName);

        savedCard = await _savedCardRepository.AddAsync(savedCard, cancellationToken);

        // Deliberately no card number, no expiry-with-code, nothing beyond the last four digits.
        _logger.LogInformation(
            $"Saved card {savedCard.Id} for a shopper ({vaulted.Brand ?? "card"} ending {vaulted.LastDigits ?? "????"}).");

        return SavedCardView.From(savedCard);
    }

    public async Task<IReadOnlyList<SavedCardView>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        var cards = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
        return cards.Select(SavedCardView.From).ToList();
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var card = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedCardByIdForBuyerSpecification(paymentMethodId, buyerId), cancellationToken);

        if (card is null)
        {
            return false;
        }

        // Remove it at the processor first. If that fails we keep the row, because a row pointing at
        // a live vault entry is recoverable — a vault entry nothing points at is not.
        await _gateway.DeleteVaultedCardAsync(card.PayPalVaultId, cancellationToken);
        await _savedCardRepository.DeleteAsync(card, cancellationToken);

        _logger.LogInformation($"Deleted saved card {paymentMethodId}.");
        return true;
    }

    private static void ValidateCard(CardDetails card)
    {
        if (card is null)
        {
            throw new PaymentValidationException("Card details are required.");
        }

        if (string.IsNullOrWhiteSpace(card.Number))
        {
            throw new PaymentValidationException("A card number is required.");
        }

        if (string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentValidationException("A card expiry is required, in YYYY-MM form.");
        }

        if (card.Expiry.Length != 7 || card.Expiry[4] != '-')
        {
            throw new PaymentValidationException($"Card expiry must be in YYYY-MM form.");
        }
    }
}
