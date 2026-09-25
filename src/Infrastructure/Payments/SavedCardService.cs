using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPaymentGateway _gateway;
    private readonly ILogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedPaymentMethod> repository,
        IPaymentGateway gateway,
        ILogger<SavedCardService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
            throw new PaymentValidationException("A card number and expiry (YYYY-MM) are required to save a card.");

        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        var payPalCustomerId = existing.FirstOrDefault()?.PayPalCustomerId;

        var command = new VaultCardCommand
        {
            Card = card,
            PayPalCustomerId = payPalCustomerId,
            MerchantCustomerId = MerchantCustomerId(buyerId),
            IdempotencyKey = Guid.NewGuid().ToString("N")
        };

        var result = await _gateway.VaultCardAsync(command, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            result.PayPalCustomerId,
            result.VaultId,
            result.Brand,
            result.LastDigits,
            result.Expiry,
            result.CardholderName);

        await _repository.AddAsync(saved, cancellationToken);
        _logger.LogInformation("Saved card {PaymentMethodId} (vault {VaultId}) for buyer.", saved.Id, saved.VaultId);

        return ToView(saved);
    }

    public async Task<IReadOnlyList<SavedCardView>> ListCardsAsync(string buyerId, CancellationToken cancellationToken)
    {
        var cards = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        return cards.OrderByDescending(c => c.CreatedAt).Select(ToView).ToList();
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var card = await _repository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpecification(paymentMethodId), cancellationToken);
        if (card is null || card.BuyerId != buyerId)
            return false;

        try
        {
            await _gateway.DeleteVaultedCardAsync(card.VaultId, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            // The card must no longer be visible/usable in this app regardless; remove it locally and
            // log if PayPal could not confirm deletion (e.g. already gone, or a transient provider error).
            _logger.LogWarning(ex, "PayPal vault delete for {VaultId} did not confirm; removing locally anyway.", card.VaultId);
        }

        await _repository.DeleteAsync(card, cancellationToken);
        _logger.LogInformation("Deleted saved card {PaymentMethodId}.", paymentMethodId);
        return true;
    }

    private static SavedCardView ToView(SavedPaymentMethod card) => new()
    {
        PaymentMethodId = card.Id,
        Brand = card.Brand,
        LastDigits = card.LastDigits,
        Expiry = card.Expiry,
        CardholderName = card.CardholderName,
        CreatedAt = card.CreatedAt
    };

    /// <summary>Stable, PayPal-safe merchant customer id derived from the shopper id (no PII on the wire).</summary>
    private static string MerchantCustomerId(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        var hex = Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
        return $"cust-{hex}";
    }
}
