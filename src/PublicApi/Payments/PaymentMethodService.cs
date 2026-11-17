using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IPaymentMethodService
{
    Task<PaymentMethodResponse> SaveAsync(SavePaymentMethodRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<PaymentMethodResponse>> ListAsync(CancellationToken cancellationToken);
    Task DeleteAsync(string paymentMethodId, CancellationToken cancellationToken);
}

public sealed class PaymentMethodService : IPaymentMethodService
{
    private readonly CatalogContext _db;
    private readonly IFinixGateway _gateway;
    private readonly IShopperIdentityService _identityService;
    private readonly ICurrentUser _currentUser;
    private readonly PaymentClaimStore _claims;
    private readonly ILogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        CatalogContext db,
        IFinixGateway gateway,
        IShopperIdentityService identityService,
        ICurrentUser currentUser,
        PaymentClaimStore claims,
        ILogger<PaymentMethodService> logger)
    {
        _db = db;
        _gateway = gateway;
        _identityService = identityService;
        _currentUser = currentUser;
        _claims = claims;
        _logger = logger;
    }

    public async Task<PaymentMethodResponse> SaveAsync(
        SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var shopperId = _currentUser.UserId;
        var card = CardValidation.Normalize(
            request.Number,
            request.ExpirationMonth,
            request.ExpirationYear,
            request.Name,
            request.SecurityCode);

        var lastFour = card.Number[^4..];
        var claimKey = $"CARD:{shopperId}:{lastFour}:{card.ExpirationMonth}:{card.ExpirationYear}";

        using var gate = await _claims.AcquireAsync(claimKey, cancellationToken);

        var claim = await _claims.FindAsync(claimKey, cancellationToken);
        if (claim is not null)
        {
            if (!string.IsNullOrWhiteSpace(claim.ReferenceId))
            {
                var existingCard = await _db.SavedCards.FirstOrDefaultAsync(
                    saved => saved.ShopperId == shopperId
                             && saved.FinixPaymentInstrumentId == claim.ReferenceId,
                    cancellationToken);

                if (existingCard is not null && !existingCard.IsDeleted)
                {
                    return ToResponse(existingCard);
                }
            }

            var identityId = await _identityService.EnsureAsync(shopperId, cancellationToken);
            var settled = await _gateway.FindCardAsync(
                identityId,
                lastFour,
                card.ExpirationMonth,
                card.ExpirationYear,
                claim.CreatedAt.AddMinutes(-2),
                cancellationToken);

            if (settled is not null)
            {
                var record = await RecordCardAsync(shopperId, card, settled, cancellationToken);
                await _claims.CompleteAsync(claim, record.FinixPaymentInstrumentId, null, cancellationToken);
                return ToResponse(record);
            }

            await _claims.RemoveAsync(claim, cancellationToken);
            claim = null;
        }

        claim = new PaymentOperationClaim(claimKey, "SAVE_CARD");
        if (!await _claims.TryAddAsync(claim, cancellationToken))
        {
            var racedClaim = await _claims.FindAsync(claimKey, cancellationToken);
            if (racedClaim is not null && !string.IsNullOrWhiteSpace(racedClaim.ReferenceId))
            {
                var racedCard = await _db.SavedCards.FirstOrDefaultAsync(
                    saved => saved.ShopperId == shopperId
                             && saved.FinixPaymentInstrumentId == racedClaim.ReferenceId,
                    cancellationToken);

                if (racedCard is not null && !racedCard.IsDeleted)
                {
                    return ToResponse(racedCard);
                }
            }

            throw new PaymentApiException(
                409,
                "This card is already being saved; retry shortly.",
                "CARD_SAVE_IN_PROGRESS");
        }

        try
        {
            var identityId = await _identityService.EnsureAsync(shopperId, cancellationToken);
            var created = await _gateway.CreateCardAsync(identityId, card, cancellationToken);

            if (string.IsNullOrWhiteSpace(created.Id))
            {
                throw new PaymentApiException(
                    502,
                    "Finix tokenized the card without returning a payment instrument id.",
                    "MISSING_PAYMENT_INSTRUMENT_ID");
            }

            var record = await RecordCardAsync(shopperId, card, created, cancellationToken);
            await _claims.CompleteAsync(claim, record.FinixPaymentInstrumentId, null, cancellationToken);

            _logger.LogInformation(
                "Saved payment instrument {PaymentMethodId} for shopper {ShopperId}.",
                record.FinixPaymentInstrumentId,
                shopperId);

            return ToResponse(record);
        }
        catch (PaymentProviderException exception)
        {
            var identityId = await _identityService.EnsureAsync(shopperId, cancellationToken);
            var settled = await _gateway.FindCardAsync(
                identityId,
                lastFour,
                card.ExpirationMonth,
                card.ExpirationYear,
                claim.CreatedAt.AddMinutes(-2),
                cancellationToken);

            if (settled is not null)
            {
                var record = await RecordCardAsync(shopperId, card, settled, cancellationToken);
                await _claims.CompleteAsync(claim, record.FinixPaymentInstrumentId, null, cancellationToken);
                return ToResponse(record);
            }

            if (exception.OutcomeUnknown)
            {
                throw new PaymentApiException(
                    502,
                    "Finix did not confirm the card save; the outcome is unknown. Repeating the request will settle it.",
                    "CARD_SAVE_UNKNOWN");
            }

            await _claims.RemoveAsync(claim, cancellationToken);
            throw new PaymentApiException(409, exception.Message, exception.Code ?? "CARD_SAVE_FAILED");
        }
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var shopperId = _currentUser.UserId;

        var cards = await _db.SavedCards
            .Where(card => card.ShopperId == shopperId && !card.IsDeleted)
            .OrderBy(card => card.CreatedAt)
            .ToListAsync(cancellationToken);

        return cards.Select(ToResponse).ToList();
    }

    public async Task DeleteAsync(string paymentMethodId, CancellationToken cancellationToken)
    {
        var shopperId = _currentUser.UserId;

        var card = await _db.SavedCards.FirstOrDefaultAsync(
            saved => saved.ShopperId == shopperId
                     && saved.FinixPaymentInstrumentId == paymentMethodId,
            cancellationToken);

        if (card is null)
        {
            throw new PaymentApiException(
                404,
                $"Saved payment method {paymentMethodId} was not found for this shopper.",
                "PAYMENT_METHOD_NOT_FOUND");
        }

        if (card.IsDeleted)
        {
            return;
        }

        var claimKey = $"DELETE_CARD:{shopperId}:{paymentMethodId}";
        using var gate = await _claims.AcquireAsync(claimKey, cancellationToken);

        var claim = await _claims.FindAsync(claimKey, cancellationToken);
        if (claim is not null)
        {
            card.MarkDeleted();
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        claim = new PaymentOperationClaim(claimKey, "DELETE_CARD");
        if (!await _claims.TryAddAsync(claim, cancellationToken))
        {
            card.MarkDeleted();
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            await _gateway.DisableCardAsync(paymentMethodId, cancellationToken);
            card.MarkDeleted();
            await _db.SaveChangesAsync(cancellationToken);
            await _claims.CompleteAsync(claim, paymentMethodId, null, cancellationToken);

            _logger.LogInformation(
                "Deleted payment instrument {PaymentMethodId} for shopper {ShopperId}.",
                paymentMethodId,
                shopperId);
        }
        catch (PaymentProviderException exception)
        {
            var providerCard = await _gateway.GetCardAsync(paymentMethodId, cancellationToken);
            if (providerCard is null || !providerCard.Enabled)
            {
                card.MarkDeleted();
                await _db.SaveChangesAsync(cancellationToken);
                await _claims.CompleteAsync(claim, paymentMethodId, null, cancellationToken);
                return;
            }

            if (exception.OutcomeUnknown)
            {
                throw new PaymentApiException(
                    502,
                    "Finix did not confirm the card deletion; the outcome is unknown. Repeating the request will settle it.",
                    "CARD_DELETE_UNKNOWN");
            }

            await _claims.RemoveAsync(claim, cancellationToken);
            throw new PaymentApiException(409, exception.Message, exception.Code ?? "CARD_DELETE_FAILED");
        }
    }

    private async Task<SavedCard> RecordCardAsync(
        string shopperId,
        CardDetails card,
        FinixCardInfo providerCard,
        CancellationToken cancellationToken)
    {
        var existing = await _db.SavedCards.FirstOrDefaultAsync(
            saved => saved.ShopperId == shopperId
                     && saved.FinixPaymentInstrumentId == providerCard.Id,
            cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var lastFour = string.IsNullOrWhiteSpace(providerCard.LastFour)
            ? card.Number[^4..]
            : providerCard.LastFour!;

        var record = new SavedCard(
            shopperId,
            providerCard.Id,
            lastFour,
            providerCard.Brand,
            providerCard.ExpirationMonth ?? card.ExpirationMonth,
            providerCard.ExpirationYear ?? card.ExpirationYear,
            providerCard.Fingerprint);

        _db.SavedCards.Add(record);
        await _db.SaveChangesAsync(cancellationToken);
        return record;
    }

    private static PaymentMethodResponse ToResponse(SavedCard card) => new()
    {
        PaymentMethodId = card.FinixPaymentInstrumentId,
        Last4 = card.Last4,
        Brand = card.Brand,
        ExpirationMonth = card.ExpirationMonth,
        ExpirationYear = card.ExpirationYear
    };
}
