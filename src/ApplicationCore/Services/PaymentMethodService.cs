using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Manages a shopper's saved cards by vaulting them with PayPal and keeping only a safe reference
/// (vault token + brand/last4/expiry) in the application. Every operation is scoped to the calling
/// shopper so saved cards are strictly private to their owner.
/// </summary>
public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalPaymentGateway _paymentGateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<PaymentMethod> paymentMethodRepository,
        IPayPalPaymentGateway paymentGateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _paymentGateway.VaultCardAsync(card, Guid.NewGuid().ToString("N"), cancellationToken);

        var paymentMethod = new PaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.Card.Brand,
            vaulted.Card.Last4,
            vaulted.Card.ExpiryMonth,
            vaulted.Card.ExpiryYear,
            card.CardholderName);

        paymentMethod = await _paymentMethodRepository.AddAsync(paymentMethod, cancellationToken);
        return paymentMethod;
    }

    public async Task<IReadOnlyCollection<PaymentMethod>> ListAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _paymentMethodRepository.ListAsync(
            new PaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var paymentMethod = await _paymentMethodRepository.FirstOrDefaultAsync(
            new PaymentMethodByIdAndBuyerSpecification(paymentMethodId, buyerId), cancellationToken);
        if (paymentMethod is null)
        {
            return false;
        }

        // Best-effort removal from PayPal's vault. If the token is already gone (or the call fails), the
        // authoritative guarantee — that the card no longer appears for, or is usable by, this shopper — is
        // still met by removing our reference below.
        try
        {
            await _paymentGateway.DeleteVaultedCardAsync(paymentMethod.CardId, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            _logger.LogWarning($"Failed to delete vaulted card {paymentMethodId} from PayPal (debugId: {ex.DebugId}); removing local reference anyway.");
        }

        await _paymentMethodRepository.DeleteAsync(paymentMethod, cancellationToken);
        return true;
    }
}
