using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<PaymentMethod> _repository;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<PaymentMethod> repository,
        IPaymentGateway gateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Local record before the provider call, carrying the per-save idempotency seed.
        var method = new PaymentMethod(buyerId);
        method = await _repository.AddAsync(method, ct);

        // Reuse this shopper's existing PayPal customer id so their cards group under one customer.
        var existing = await _repository.ListAsync(new PaymentMethodsByBuyerSpecification(buyerId), ct);
        var customerId = existing.FirstOrDefault(m => m.PayPalCustomerId is not null)?.PayPalCustomerId;

        VaultCardResult result;
        try
        {
            result = await _gateway.VaultCardAsync(card, customerId, $"{method.IdempotencyKey}-vault", ct);
        }
        catch
        {
            // The vault call did not produce a usable token — remove the empty local row so it never
            // appears as a saved card. (No provider effect is stranded: no token was created.)
            await _repository.DeleteAsync(method, ct);
            throw;
        }

        method.SetVaultResult(result.TokenId, result.CustomerId, result.Brand, result.LastDigits,
            result.Expiry, result.CardholderName);
        await _repository.UpdateAsync(method, ct);
        _logger.LogInformation($"Saved card {method.Id} for {buyerId}: {result.Brand} ****{result.LastDigits} (vault token {result.TokenId}).");
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var methods = await _repository.ListAsync(new PaymentMethodsByBuyerSpecification(buyerId), ct);
        // Only fully-vaulted cards are shown (a row without a token never completed its vault call).
        return methods.Where(m => !string.IsNullOrEmpty(m.PayPalVaultTokenId)).ToList();
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var method = await _repository.FirstOrDefaultAsync(
            new PaymentMethodByIdAndBuyerSpecification(paymentMethodId, buyerId), ct);
        if (method is null)
            return false;

        // Remove at PayPal first (idempotent — a missing token is treated as already gone), then locally
        // so the card can no longer be resolved to pay.
        if (!string.IsNullOrEmpty(method.PayPalVaultTokenId))
            await _gateway.DeleteVaultedCardAsync(method.PayPalVaultTokenId, ct);

        await _repository.DeleteAsync(method, ct);
        _logger.LogInformation($"Removed saved card {paymentMethodId} for {buyerId}.");
        return true;
    }
}
