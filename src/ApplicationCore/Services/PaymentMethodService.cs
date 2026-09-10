using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Saves, lists and removes a shopper's cards (Flow 2). The card is vaulted at PayPal; this app keeps
/// only the vault token and a safe description. Every operation is scoped to the owning shopper.
/// </summary>
public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedCard> savedCardRepository,
        IPayPalGateway payPal,
        IAppLogger<PaymentMethodService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedCard> SaveCardAsync(
        string buyerId, SaveCardCommand command, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(command, nameof(command));
        Guard.Against.NullOrEmpty(command.Number, nameof(command.Number));
        Guard.Against.NullOrEmpty(command.Expiry, nameof(command.Expiry));

        var card = new PayPalRawCard(
            Number: command.Number,
            Expiry: command.Expiry,
            SecurityCode: command.SecurityCode,
            Name: command.CardholderName,
            BillingAddress: BuildBillingAddress(command));

        var vaulted = await _payPal.VaultCardAsync(
            card, merchantCustomerId: buyerId, idempotencyKey: Guid.NewGuid().ToString("N"), cancellationToken);

        var savedCard = new SavedCard(
            buyerId, vaulted.VaultId, vaulted.Brand, vaulted.Last4, vaulted.Expiry, command.CardholderName);
        savedCard = await _savedCardRepository.AddAsync(savedCard, cancellationToken);

        _logger.LogInformation($"Saved card {savedCard.Id} for buyer (vault {vaulted.VaultId}, {vaulted.Brand} ****{vaulted.Last4}).");
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> ListCardsAsync(
        string buyerId, CancellationToken cancellationToken = default)
    {
        var cards = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
        return cards;
    }

    public async Task DeleteCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default)
    {
        var card = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedCardByIdForBuyerSpecification(savedCardId, buyerId), cancellationToken)
            ?? throw new EntityNotFoundException($"Saved card {savedCardId} was not found.");

        // Remove from PayPal's vault first so the card can no longer be charged, then drop our record.
        await _payPal.DeleteVaultedCardAsync(card.PayPalVaultId, cancellationToken);
        await _savedCardRepository.DeleteAsync(card, cancellationToken);

        _logger.LogInformation($"Deleted saved card {savedCardId} for buyer (vault {card.PayPalVaultId}).");
    }

    private static PayPalBillingAddress? BuildBillingAddress(SaveCardCommand c)
    {
        if (string.IsNullOrWhiteSpace(c.CountryCode) && string.IsNullOrWhiteSpace(c.AddressLine1)
            && string.IsNullOrWhiteSpace(c.PostalCode))
        {
            return null;
        }

        return new PayPalBillingAddress(
            AddressLine1: c.AddressLine1,
            AddressLine2: c.AddressLine2,
            AdminArea2: c.City,
            AdminArea1: c.State,
            PostalCode: c.PostalCode,
            CountryCode: string.IsNullOrWhiteSpace(c.CountryCode) ? "US" : c.CountryCode!);
    }
}
