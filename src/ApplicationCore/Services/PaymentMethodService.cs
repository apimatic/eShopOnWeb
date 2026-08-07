using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<PaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<SaveCardResult> SaveCardAsync(string ownerId, CardDetails card, string? alias, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.Null(card, nameof(card));

        // Store the card in PayPal's vault; a fresh idempotency key per save request avoids
        // accidental token reuse across distinct cards while still guarding a single retried call.
        var vaultRequest = new VaultCardRequest
        {
            Card = card,
            CustomerReference = ownerId,
            IdempotencyKey = $"vault-{Guid.NewGuid():N}"
        };

        var vaultResult = await _paymentGateway.VaultCardAsync(vaultRequest, cancellationToken);
        if (!vaultResult.Success || string.IsNullOrEmpty(vaultResult.VaultToken))
        {
            _logger.LogWarning($"Vaulting card for owner failed. DebugId: {vaultResult.DebugId ?? "n/a"}");
            return new SaveCardResult(SaveCardOutcome.GatewayError, Error: vaultResult.ErrorMessage ?? "Unable to save card with the payment provider.");
        }

        var last4 = !string.IsNullOrEmpty(vaultResult.Last4)
            ? vaultResult.Last4!
            : LastFour(card.Number);

        var paymentMethod = new PaymentMethod(
            ownerId: ownerId,
            vaultToken: vaultResult.VaultToken!,
            last4: last4,
            brand: vaultResult.Brand,
            expiryMonthYear: vaultResult.ExpiryMonthYear ?? card.ExpiryMonthYear,
            cardholderName: vaultResult.CardholderName ?? card.CardholderName,
            alias: alias);

        await _paymentMethodRepository.AddAsync(paymentMethod, cancellationToken);

        return new SaveCardResult(SaveCardOutcome.Saved, paymentMethod);
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListForOwnerAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        var spec = new PaymentMethodsByOwnerSpecification(ownerId);
        return await _paymentMethodRepository.ListAsync(spec, cancellationToken);
    }

    public async Task<DeleteCardResult> DeleteAsync(string ownerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        // Owner is part of the query, so another shopper's card is simply not found here.
        var spec = new PaymentMethodByIdForOwnerSpecification(ownerId, paymentMethodId);
        var paymentMethod = await _paymentMethodRepository.FirstOrDefaultAsync(spec, cancellationToken);
        if (paymentMethod is null)
        {
            return new DeleteCardResult(DeleteCardOutcome.NotFound);
        }

        // Best-effort removal from the PayPal vault so the token can no longer be used to pay.
        // Removal from our own store (below) is the authoritative guarantee that it disappears
        // from the shopper's saved cards and can no longer be selected for payment.
        try
        {
            await _paymentGateway.DeleteVaultedCardAsync(paymentMethod.VaultToken, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to delete vaulted token at PayPal for payment method {paymentMethodId}: {ex.Message}");
        }

        await _paymentMethodRepository.DeleteAsync(paymentMethod, cancellationToken);

        return new DeleteCardResult(DeleteCardOutcome.Deleted);
    }

    private static string LastFour(string number)
    {
        var digitsOnly = number ?? string.Empty;
        return digitsOnly.Length <= 4 ? digitsOnly : digitsOnly.Substring(digitsOnly.Length - 4);
    }
}
