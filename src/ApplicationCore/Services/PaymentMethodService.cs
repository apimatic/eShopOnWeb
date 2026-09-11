using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NotFoundException = Microsoft.eShopWeb.ApplicationCore.Exceptions.NotFoundException;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Saves, lists and removes a shopper's vaulted cards. The card is vaulted at PayPal; only a safe
/// description (brand, last four, expiry) and PayPal's vault token id are kept in the app database.
/// </summary>
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

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var payPalCustomerId = DeriveCustomerId(buyerId);
        var idempotencyKey = $"vault-{payPalCustomerId}-{Guid.NewGuid():N}";

        var result = await _paymentGateway.VaultCardAsync(card, payPalCustomerId, idempotencyKey, cancellationToken);

        var method = new PaymentMethod(buyerId, result.VaultTokenId, result.PayPalCustomerId,
            result.CardBrand, result.CardLast4, result.CardExpiry);
        method = await _paymentMethodRepository.AddAsync(method, cancellationToken);

        _logger.LogInformation($"Saved card {method.Description} for {buyerId} (vault token {result.VaultTokenId}).");
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        return await _paymentMethodRepository.ListAsync(new PaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var method = await _paymentMethodRepository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (method is null || method.BuyerId != buyerId)
        {
            // Report as not-found so a shopper cannot probe or delete another shopper's cards.
            throw new NotFoundException($"Saved card {paymentMethodId} was not found.");
        }

        await _paymentGateway.DeleteVaultTokenAsync(method.VaultTokenId, cancellationToken);
        await _paymentMethodRepository.DeleteAsync(method, cancellationToken);

        _logger.LogInformation($"Removed saved card {paymentMethodId} for {buyerId}.");
    }

    /// <summary>
    /// A stable, PayPal-safe customer id per shopper so a shopper's cards are grouped under one
    /// PayPal customer. Derived deterministically from the buyer id; contains no personal data.
    /// </summary>
    private static string DeriveCustomerId(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        var hex = Convert.ToHexString(hash);
        return "eshop" + hex.Substring(0, 17).ToLowerInvariant();
    }
}
