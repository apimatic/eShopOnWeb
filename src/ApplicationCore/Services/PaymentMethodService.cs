using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private static readonly Regex ExpiryFormat = new(@"^\d{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);

    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedCard> savedCardRepository,
        IPaymentGateway paymentGateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        ValidateCard(card);

        // Reuse the shopper's existing PayPal customer id when they already vaulted a card.
        var existing = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), ct);
        var payPalCustomerId = existing.FirstOrDefault()?.PayPalCustomerId;

        var result = await _paymentGateway.VaultCardAsync(card, payPalCustomerId, buyerId,
            $"vault-{buyerId}-{Guid.NewGuid():N}", ct);

        var savedCard = new SavedCard(buyerId, result.PayPalCustomerId, result.PaymentTokenId,
            result.Brand, result.LastDigits, result.Expiry);
        savedCard = await _savedCardRepository.AddAsync(savedCard, ct);

        _logger.LogInformation("Saved card ending {LastDigits} for buyer.", savedCard.LastDigits ?? "????");
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), ct);
    }

    public async Task DeleteCardAsync(string buyerId, int savedCardId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var savedCard = await _savedCardRepository.GetByIdAsync(savedCardId, ct);
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            throw new SavedCardNotFoundException(savedCardId);
        }

        try
        {
            await _paymentGateway.DeleteVaultedCardAsync(savedCard.PayPalPaymentTokenId, ct);
        }
        catch (PaymentGatewayException ex) when (ex.ProviderStatusCode == 404)
        {
            // Already gone at PayPal — converge locally.
            _logger.LogWarning("Vault token for saved card {SavedCardId} was already gone at PayPal.", savedCardId);
        }

        await _savedCardRepository.DeleteAsync(savedCard, ct);
        _logger.LogInformation("Deleted saved card {SavedCardId} for buyer.", savedCardId);
    }

    private static void ValidateCard(CardDetails card)
    {
        Guard.Against.Null(card, nameof(card));

        var digitsOnly = new string((card.Number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digitsOnly.Length is < 13 or > 19)
        {
            throw new BadRequestException("The card number is invalid.");
        }
        if (string.IsNullOrWhiteSpace(card.Expiry) || !ExpiryFormat.IsMatch(card.Expiry))
        {
            throw new BadRequestException("The card expiry must be in YYYY-MM format.");
        }
        var expiry = DateOnly.ParseExact(card.Expiry, "yyyy-MM", null);
        if (expiry < DateOnly.FromDateTime(DateTime.UtcNow.Date))
        {
            throw new BadRequestException("The card expiry is in the past.");
        }
        if (card.Address is not null && string.IsNullOrWhiteSpace(card.Address.CountryCode))
        {
            throw new BadRequestException("The billing address requires a country code.");
        }
    }
}
