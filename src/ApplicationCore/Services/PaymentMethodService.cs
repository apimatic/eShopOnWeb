using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<PaymentMethod> _repository;
    private readonly IPayPalGateway _gateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<PaymentMethod> repository,
        IPayPalGateway gateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<Result<PaymentMethodViewModel>> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (card is null || string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry) || string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            return Result<PaymentMethodViewModel>.Error("Card number, expiry and security code are required to save a card.");
        }

        var existing = await _repository.ListAsync(new PaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        var existingCustomerId = existing.Select(p => p.PayPalCustomerId).FirstOrDefault(id => !string.IsNullOrEmpty(id));

        var merchantCustomerId = DeriveMerchantCustomerId(buyerId);
        // A fresh key per save: vaulting the same card twice is a distinct action, and a key reused across
        // runs would replay a prior (possibly deleted) token at PayPal.
        var idempotencyKey = Guid.NewGuid().ToString("N");

        try
        {
            var vaulted = await _gateway.VaultCardAsync(card, merchantCustomerId, existingCustomerId, idempotencyKey, cancellationToken);

            // A double-submit of the same card yields the same token id — don't create a second saved card.
            var already = existing.FirstOrDefault(p => p.PayPalVaultId == vaulted.PaymentTokenId);
            if (already is not null)
            {
                return PaymentMapping.ToViewModel(already);
            }

            var paymentMethod = new PaymentMethod(
                buyerId,
                vaulted.PaymentTokenId,
                vaulted.CustomerId ?? existingCustomerId,
                vaulted.CardBrand,
                vaulted.LastFourDigits,
                vaulted.Expiry,
                vaulted.CardholderName);

            paymentMethod = await _repository.AddAsync(paymentMethod, cancellationToken);
            return PaymentMapping.ToViewModel(paymentMethod);
        }
        catch (PayPalException ex)
        {
            _logger.LogWarning($"Vaulting a card for {buyerId} failed: {ex.Message}");
            return Result<PaymentMethodViewModel>.Error($"The card could not be saved: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<PaymentMethodViewModel>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var methods = await _repository.ListAsync(new PaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        return methods.Select(PaymentMapping.ToViewModel).ToList();
    }

    public async Task<Result> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var paymentMethod = await _repository.FirstOrDefaultAsync(new PaymentMethodByIdSpec(paymentMethodId), cancellationToken);
        if (paymentMethod is null || !string.Equals(paymentMethod.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Do not reveal another shopper's card.
            return Result.NotFound();
        }

        try
        {
            await _gateway.DeleteVaultedCardAsync(paymentMethod.PayPalVaultId, cancellationToken);
        }
        catch (PayPalException ex) when (ex.StatusCode == 404)
        {
            // Already gone at PayPal — fine, continue removing it locally.
            _logger.LogWarning($"Vaulted card {paymentMethod.PayPalVaultId} was already absent at PayPal.");
        }
        catch (PayPalException ex)
        {
            return Result.Error($"The card could not be removed: {ex.Message}");
        }

        await _repository.DeleteAsync(paymentMethod, cancellationToken);
        return Result.Success();
    }

    private static string DeriveMerchantCustomerId(string buyerId)
    {
        var hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buyerId))).ToLowerInvariant();
        return "cust-" + hex[..16];
    }
}
