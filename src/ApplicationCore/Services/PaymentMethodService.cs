using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payment;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedCard> _savedCards;
    private readonly IFinixClient _finix;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedCard> savedCards,
        IFinixClient finix,
        IAppLogger<PaymentMethodService> logger)
    {
        _savedCards = savedCards;
        _finix = finix;
        _logger = logger;
    }

    public async Task<SavedCard> SaveAsync(string buyerId, SaveCardCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(401, "A signed-in shopper is required to save a card.");
        }

        ValidateCard(command.Card);

        FinixIdentity identity;
        FinixPaymentInstrument instrument;
        try
        {
            identity = await _finix.CreateIdentityAsync(command.Card, cancellationToken);
            instrument = await _finix.CreatePaymentInstrumentAsync(identity.Id, command.Card, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw new PaymentOperationException(
                ex.StatusCode >= 500 ? 502 : 402,
                $"Saving the card failed: {ex.Message}",
                ex.Code,
                ex.LogRef);
        }

        var card = new SavedCard(
            buyerId,
            identity.Id,
            instrument.Id,
            instrument.Brand ?? "UNKNOWN",
            instrument.LastFour ?? "0000",
            instrument.ExpirationMonth,
            instrument.ExpirationYear,
            instrument.Name ?? command.Card.Name,
            command.Alias,
            DateTimeOffset.UtcNow);

        await _savedCards.AddAsync(card, cancellationToken);
        _logger.LogInformation("Saved payment method {PaymentMethodId} for buyer {BuyerId}.", card.Id, buyerId);
        return card;
    }

    public async Task<IReadOnlyList<SavedCard>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _savedCards.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var card = await _savedCards.FirstOrDefaultAsync(
            new SavedCardByIdAndBuyerSpecification(paymentMethodId, buyerId),
            cancellationToken);

        if (card == null)
        {
            return false;
        }

        if (card.IsDeleted)
        {
            return true;
        }

        try
        {
            await _finix.DisablePaymentInstrumentAsync(card.FinixPaymentInstrumentId, cancellationToken);
        }
        catch (FinixApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning(
                "Finix payment instrument {PaymentInstrumentId} was already absent while deleting saved card {PaymentMethodId}.",
                card.FinixPaymentInstrumentId,
                card.Id);
        }
        catch (FinixApiException ex)
        {
            throw new PaymentOperationException(
                ex.StatusCode >= 500 ? 502 : 402,
                $"Disabling the saved card at Finix failed: {ex.Message}",
                ex.Code,
                ex.LogRef);
        }

        card.MarkDeleted(DateTimeOffset.UtcNow);
        await _savedCards.UpdateAsync(card, cancellationToken);
        _logger.LogInformation("Deleted payment method {PaymentMethodId} for buyer {BuyerId}.", card.Id, buyerId);
        return true;
    }

    private static void ValidateCard(CardPaymentDetails card)
    {
        if (card == null || string.IsNullOrWhiteSpace(card.Number) || card.Number.Count(char.IsDigit) < 12)
        {
            throw new PaymentOperationException(400, "A valid card number is required.");
        }

        if (card.ExpirationMonth < 1 || card.ExpirationMonth > 12)
        {
            throw new PaymentOperationException(400, "Card expiration month must be between 1 and 12.");
        }

        if (card.ExpirationYear < DateTimeOffset.UtcNow.Year)
        {
            throw new PaymentOperationException(400, "Card expiration year must not be in the past.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentOperationException(400, "The cardholder name is required.");
        }
    }
}
