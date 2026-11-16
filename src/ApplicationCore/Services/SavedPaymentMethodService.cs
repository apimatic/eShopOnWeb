using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Finix;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IFinixClient _finix;
    private readonly ILogger<SavedPaymentMethodService> _logger;

    public SavedPaymentMethodService(IRepository<SavedPaymentMethod> repository, IFinixClient finix,
        ILogger<SavedPaymentMethodService> logger)
    {
        _repository = repository;
        _finix = finix;
        _logger = logger;
    }

    public async Task<SavedPaymentMethodView> SaveAsync(string buyerId, CardInput card,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(401, "UNAUTHENTICATED", "A signed-in shopper is required.");
        }

        ValidateCard(card);

        try
        {
            var identity = await _finix.CreateBuyerIdentityAsync(buyerId, card.Name, cancellationToken);

            var instrument = await _finix.CreatePaymentInstrumentAsync(new FinixCreatePaymentInstrumentRequest
            {
                Identity = identity.Id,
                Name = card.Name.Trim(),
                Number = NormalizeCardNumber(card.Number),
                ExpirationMonth = card.ExpirationMonth,
                ExpirationYear = card.ExpirationYear,
                SecurityCode = card.SecurityCode,
                Address = ToFinixAddress(card.Address),
                Tags = new Dictionary<string, string> { ["buyerId"] = buyerId }
            }, cancellationToken);

            var saved = new SavedPaymentMethod(
                buyerId,
                instrument.Id,
                identity.Id,
                instrument.Brand,
                instrument.LastFour,
                instrument.ExpirationMonth ?? card.ExpirationMonth,
                instrument.ExpirationYear ?? card.ExpirationYear,
                card.Name.Trim());

            saved = await _repository.AddAsync(saved, cancellationToken);
            return ToView(saved);
        }
        catch (FinixApiException ex)
        {
            throw new PaymentOperationException(502, ex.Code ?? "FINIX_ERROR",
                $"Finix could not save the card: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<SavedPaymentMethodView>> ListAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var methods = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        return methods.Select(ToView).ToList();
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var method = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId), cancellationToken);

        if (method == null)
        {
            throw new PaymentOperationException(404, "PAYMENT_METHOD_NOT_FOUND", "Saved card not found.");
        }

        try
        {
            await _finix.DisablePaymentInstrumentAsync(method.FinixPaymentInstrumentId, cancellationToken);
        }
        catch (Exception ex) when (ex is FinixApiException or InvalidOperationException)
        {
            // The local record is still removed so the card cannot be used through this API.
            _logger.LogWarning(ex, "Could not disable Finix payment instrument {PaymentInstrumentId}; removing it locally.",
                method.FinixPaymentInstrumentId);
        }

        method.Delete();
        await _repository.UpdateAsync(method, cancellationToken);
    }

    private static void ValidateCard(CardInput card)
    {
        if (card == null)
        {
            throw new PaymentOperationException(400, "CARD_REQUIRED", "Card details are required.");
        }

        var number = NormalizeCardNumber(card.Number);
        if (number.Length is < 12 or > 19 || !number.All(char.IsDigit))
        {
            throw new PaymentOperationException(400, "INVALID_CARD_NUMBER", "The card number is invalid.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentOperationException(400, "INVALID_EXPIRATION", "Expiration month must be between 1 and 12.");
        }

        if (card.ExpirationYear < 2000)
        {
            throw new PaymentOperationException(400, "INVALID_EXPIRATION", "Expiration year must be a four-digit year.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentOperationException(400, "INVALID_CARDHOLDER", "The cardholder name is required.");
        }
    }

    internal static string NormalizeCardNumber(string? number) =>
        (number ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).Trim();

    internal static FinixAddress? ToFinixAddress(CardAddressInput? address)
    {
        if (address == null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(address.Line1) || string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Region) || string.IsNullOrWhiteSpace(address.PostalCode) ||
            string.IsNullOrWhiteSpace(address.Country))
        {
            return null;
        }

        return new FinixAddress
        {
            Line1 = address.Line1,
            Line2 = address.Line2,
            City = address.City,
            Region = address.Region,
            PostalCode = address.PostalCode,
            Country = address.Country
        };
    }

    internal static SavedPaymentMethodView ToView(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand ?? string.Empty,
        Last4 = method.Last4 ?? string.Empty,
        ExpirationMonth = method.ExpirationMonth,
        ExpirationYear = method.ExpirationYear,
        CardholderName = method.CardholderName,
        FinixPaymentInstrumentId = method.FinixPaymentInstrumentId,
        CreatedAt = method.CreatedAt
    };
}
