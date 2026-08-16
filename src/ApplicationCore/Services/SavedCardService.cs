using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using static Microsoft.eShopWeb.ApplicationCore.Services.ServiceResults;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPalGateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalGateway payPalGateway,
        IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _payPalGateway = payPalGateway;
        _logger = logger;
    }

    public async Task<Result<SavedPaymentMethod>> SaveCardAsync(
        string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        var validation = ValidateCard(card);
        if (validation is not null)
        {
            return Invalid<SavedPaymentMethod>(validation);
        }

        // Reuse the shopper's existing PayPal customer id so all their cards group under one customer.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        var customerId = existing.Select(e => e.PayPalCustomerId).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

        PayPalVaultedCard vaulted;
        try
        {
            vaulted = await _payPalGateway.VaultCardAsync(card, customerId, $"eshop-vault-{Guid.NewGuid():N}", cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Vaulting a card for buyer {0} failed: {1} (debug id {2}).", buyerId, ex.Message, ex.DebugId ?? "n/a");
            return Result<SavedPaymentMethod>.Error($"PayPal could not save the card: {ex.Message}");
        }

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.CustomerId ?? customerId,
            vaulted.Brand,
            vaulted.LastDigits,
            vaulted.Expiry,
            vaulted.Name ?? card.Name);

        saved = await _repository.AddAsync(saved, cancellationToken);
        _logger.LogInformation("Saved card {0} for buyer {1} (vault {2}).", saved.Id, buyerId, vaulted.VaultId);

        return Result<SavedPaymentMethod>.Success(saved);
    }

    public async Task<Result<IReadOnlyList<SavedPaymentMethod>>> ListForBuyerAsync(
        string buyerId, CancellationToken cancellationToken = default)
    {
        var cards = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        return Result<IReadOnlyList<SavedPaymentMethod>>.Success(cards);
    }

    public async Task<Result> DeleteAsync(
        string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var card = await _repository.GetByIdAsync(paymentMethodId, cancellationToken);

        // A saved card belongs to the shopper who saved it — another shopper can neither see nor delete it.
        if (card is null || card.BuyerId != buyerId)
        {
            return Result.NotFound();
        }

        try
        {
            await _payPalGateway.DeleteVaultedCardAsync(card.PayPalVaultId, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Deleting vault token {0} failed: {1} (debug id {2}).", card.PayPalVaultId, ex.Message, ex.DebugId ?? "n/a");
            return Result.Error($"PayPal could not remove the saved card: {ex.Message}");
        }

        await _repository.DeleteAsync(card, cancellationToken);
        _logger.LogInformation("Deleted saved card {0} for buyer {1}.", paymentMethodId, buyerId);

        return Result.Success();
    }

    private static string? ValidateCard(CardDetails card)
    {
        var digits = new string((card.Number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length < 12 || digits.Length > 19)
        {
            return "Card number is not valid.";
        }
        var parts = (card.Expiry ?? string.Empty).Split('-');
        var validExpiry = parts.Length == 2
            && int.TryParse(parts[0], out var year) && year is >= 2000 and <= 2100
            && int.TryParse(parts[1], out var month) && month is >= 1 and <= 12;
        return validExpiry ? null : "Card expiry must be in the format YYYY-MM.";
    }
}
