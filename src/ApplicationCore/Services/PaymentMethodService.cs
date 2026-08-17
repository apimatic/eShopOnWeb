using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using static Microsoft.eShopWeb.ApplicationCore.Services.PaymentResults;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Manages saved (vaulted) cards. Card details are sent straight to PayPal's Vault; only the resulting
/// token and a safe descriptor (brand, last four, expiry) are stored here. Every operation is scoped to
/// the calling shopper.
/// </summary>
public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalClient _payPal;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalClient payPal,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<Result<SavedCardView>> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Result<SavedCardView>.Unauthorized();
        }

        if (card is null)
        {
            return Invalid<SavedCardView>("Card details are required.");
        }

        var normalized = PaymentMapping.NormalizeExpiry(card.Expiry);
        if (normalized is null)
        {
            return Invalid<SavedCardView>("Card expiry must be a valid date (YYYY-MM or MM/YY).");
        }

        var digits = (card.Number ?? string.Empty).Replace(" ", string.Empty);
        if (digits.Length < 12 || !digits.All(char.IsDigit))
        {
            return Invalid<SavedCardView>("A valid card number is required.");
        }

        if (string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            return Invalid<SavedCardView>("A card security code is required.");
        }

        var toVault = card with { Expiry = normalized, Number = digits };
        var customerId = BuildCustomerId(buyerId);

        try
        {
            var vaulted = await _payPal.VaultCardAsync(toVault, customerId, $"vault-{Guid.NewGuid():N}", ct);
            var saved = new SavedPaymentMethod(
                buyerId,
                vaulted.PaymentTokenId,
                vaulted.CustomerId ?? customerId,
                string.IsNullOrWhiteSpace(vaulted.Brand) ? "CARD" : vaulted.Brand,
                string.IsNullOrWhiteSpace(vaulted.LastDigits) ? digits[^4..] : vaulted.LastDigits,
                string.IsNullOrWhiteSpace(vaulted.Expiry) ? normalized : vaulted.Expiry,
                vaulted.CardholderName ?? card.CardholderName);

            saved = await _repository.AddAsync(saved, ct);
            _logger.LogInformation("Saved card {0} for {1}: {2} ****{3}", saved.Id, buyerId, saved.Brand, saved.LastFourDigits);
            return Result<SavedCardView>.Success(ToView(saved));
        }
        catch (PayPalApiException ex) when (ex.IsInstrumentDeclined)
        {
            _logger.LogWarning("Card could not be vaulted for {0}: issue={1} debug_id={2}", buyerId, ex.IssueCode, ex.DebugId);
            return Invalid<SavedCardView>("The card could not be saved (declined). Please check the details.");
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Vaulting failed for {0}: {1} debug_id={2}", buyerId, ex.Message, ex.DebugId);
            return Result<SavedCardView>.Error($"PayPal could not save the card: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<SavedCardView>>> ListCardsAsync(string buyerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Result<IReadOnlyList<SavedCardView>>.Unauthorized();
        }

        var cards = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
        IReadOnlyList<SavedCardView> views = cards.Select(ToView).ToList();
        return Result<IReadOnlyList<SavedCardView>>.Success(views);
    }

    public async Task<Result> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Result.Unauthorized();
        }

        var card = await _repository.GetByIdAsync(paymentMethodId, ct);

        // A card that isn't the caller's is reported as not found so its existence is never revealed.
        if (card is null || !string.Equals(card.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return Result.NotFound($"No saved card found with id {paymentMethodId}.");
        }

        try
        {
            await _payPal.DeleteVaultedCardAsync(card.PayPalVaultId, ct);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == 404)
        {
            // Already gone at PayPal — fall through and remove the local record so it can't be used.
            _logger.LogWarning("Vault token for card {0} was already absent at PayPal.", paymentMethodId);
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Vault delete failed for card {0}: {1} debug_id={2}", paymentMethodId, ex.Message, ex.DebugId);
            return Result.Error($"PayPal could not delete the card: {ex.Message}");
        }

        await _repository.DeleteAsync(card, ct);
        _logger.LogInformation("Deleted saved card {0} for {1}.", paymentMethodId, buyerId);
        return Result.Success();
    }

    private static SavedCardView ToView(SavedPaymentMethod card) =>
        new(card.Id, card.Brand, card.LastFourDigits, card.Expiry, card.CardholderName, card.CreatedAt);

    /// <summary>A stable, non-reversible PayPal customer id so a shopper's vaulted cards are grouped together.</summary>
    private static string BuildCustomerId(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        return "eshop-" + Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }
}
