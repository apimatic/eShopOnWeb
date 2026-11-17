using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class PaymentMethodService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IFinixClient _finixClient;
    private readonly ILogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedCard> savedCardRepository,
        IFinixClient finixClient,
        ILogger<PaymentMethodService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _finixClient = finixClient;
        _logger = logger;
    }

    public async Task<SavedPaymentMethodResponse> SaveAsync(
        string buyerId,
        SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Card is null)
        {
            throw new PaymentValidationException("Card details are required.");
        }

        OrderPaymentService.ValidateCard(request.Card);

        try
        {
            var identity = await _finixClient.CreateBuyerIdentityAsync(buyerId, cancellationToken);
            var instrument = await _finixClient.CreatePaymentInstrumentAsync(
                OrderPaymentService.MapCard(request.Card, identity.Id),
                cancellationToken);

            var savedCard = new SavedCard(
                buyerId,
                instrument.Id,
                instrument.Brand,
                instrument.LastFour ?? LastFour(request.Card.Number),
                instrument.ExpirationMonth ?? request.Card.ExpirationMonth,
                instrument.ExpirationYear ?? OrderPaymentService.NormalizeYear(request.Card.ExpirationYear),
                instrument.Name ?? request.Card.Name);

            savedCard = await _savedCardRepository.AddAsync(savedCard, cancellationToken);

            _logger.LogInformation(
                "Saved Finix payment instrument {PaymentInstrumentId} as payment method {PaymentMethodId} for buyer {BuyerId}.",
                instrument.Id,
                savedCard.Id,
                buyerId);

            return ToResponse(savedCard);
        }
        catch (FinixApiException ex)
        {
            throw new PaymentConflictException($"Finix could not save the card: {ex.Message}");
        }
    }

    public async Task<SavedPaymentMethodsResponse> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        var cards = await _savedCardRepository.ListAsync(
            new SavedCardsByBuyerSpecification(buyerId),
            cancellationToken);

        return new SavedPaymentMethodsResponse
        {
            PaymentMethods = cards.Select(ToResponse).ToList()
        };
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedCardByIdAndBuyerSpecification(paymentMethodId, buyerId),
            cancellationToken);

        if (savedCard is null)
        {
            throw new PaymentResourceNotFoundException(
                $"Saved payment method {paymentMethodId} was not found.");
        }

        try
        {
            await _finixClient.SetPaymentInstrumentEnabledAsync(
                savedCard.FinixPaymentInstrumentId,
                enabled: false,
                cancellationToken);
        }
        catch (FinixApiException ex)
        {
            _logger.LogWarning(
                "Finix could not disable payment instrument {PaymentInstrumentId} while deleting local payment method {PaymentMethodId}: {Message}",
                savedCard.FinixPaymentInstrumentId,
                savedCard.Id,
                ex.Message);
        }

        await _savedCardRepository.DeleteAsync(savedCard, cancellationToken);

        _logger.LogInformation(
            "Deleted saved payment method {PaymentMethodId} for buyer {BuyerId}.",
            paymentMethodId,
            buyerId);
    }

    private static SavedPaymentMethodResponse ToResponse(SavedCard card)
    {
        return new SavedPaymentMethodResponse
        {
            PaymentMethodId = card.Id,
            Brand = card.Brand ?? "UNKNOWN",
            Last4 = card.Last4,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            CardholderName = card.CardholderName,
            CreatedAt = card.CreatedAt
        };
    }

    private static string LastFour(string? cardNumber)
    {
        var digits = OrderPaymentService.NormalizeCardNumber(cardNumber);
        return digits.Length <= 4 ? digits : digits[^4..];
    }
}

