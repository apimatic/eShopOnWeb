using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _payPalGateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalGateway payPalGateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _paymentMethodRepository = paymentMethodRepository;
        _payPalGateway = payPalGateway;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));
        Guard.Against.NullOrEmpty(card.Number, nameof(card.Number));

        // A fresh idempotency key per save attempt — each saved card is a distinct resource.
        var idempotencyKey = $"vault-{Guid.NewGuid():N}";

        var vaulted = await _payPalGateway.VaultCardAsync(ToCustomerId(buyerId), card, idempotencyKey, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.Brand,
            vaulted.Last4,
            vaulted.Expiry,
            card.CardholderName);

        await _paymentMethodRepository.AddAsync(saved, cancellationToken);

        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpecification(paymentMethodId, buyerId), cancellationToken);

        if (saved is null)
        {
            throw new PaymentMethodNotFoundException($"Saved payment method {paymentMethodId} was not found.");
        }

        // Best-effort removal from PayPal's vault. Even if the remote delete fails, we still remove the
        // local record so the card can no longer be listed or used to pay through this application.
        try
        {
            await _payPalGateway.DeleteVaultedCardAsync(saved.PayPalVaultId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to delete vaulted card at PayPal for payment method {paymentMethodId}; removing local record anyway. {ex.Message}");
        }

        await _paymentMethodRepository.DeleteAsync(saved, cancellationToken);
    }

    /// <summary>
    /// Derives a PayPal-safe merchant customer id from the shopper's identity. PayPal restricts this
    /// field to [0-9a-zA-Z_-], so any other character (e.g. '@' or '.' in an email) is replaced.
    /// </summary>
    private static string ToCustomerId(string buyerId)
    {
        var sb = new StringBuilder(buyerId.Length);
        foreach (var ch in buyerId)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '-');
        }
        var result = sb.ToString();
        return result.Length > 256 ? result[..256] : result;
    }
}
