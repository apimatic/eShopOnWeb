using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Saved cards: the card itself lives in PayPal's vault; eShop keeps only the vault token and what the shopper
/// needs to recognise the card (brand, last digits, expiry).
/// </summary>
public class PaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _methods;
    private readonly IPaymentClaimStore _claims;
    private readonly IPaymentGateway _gateway;
    private readonly TimeProvider _clock;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(IRepository<SavedPaymentMethod> methods, IPaymentClaimStore claims, IPaymentGateway gateway,
        TimeProvider clock, IAppLogger<PaymentMethodService> logger)
    {
        _methods = methods;
        _claims = claims;
        _gateway = gateway;
        _clock = clock;
        _logger = logger;
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    public Task<List<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken ct) =>
        _methods.ListAsync(new SavedPaymentMethodsForBuyerSpec(buyerId), ct);

    public async Task<SavedPaymentMethod> SaveAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        CardValidation.Validate(card, Now);

        // Reuse the shopper's PayPal customer so all their cards sit under one vault customer.
        var known = await _methods.ListAsync(new SavedPaymentMethodsForBuyerSpec(buyerId, activeOnly: false), ct);
        var customerId = known.Select(m => m.PayPalCustomerId).FirstOrDefault(id => !string.IsNullOrEmpty(id));

        var method = new SavedPaymentMethod(buyerId, Guid.NewGuid().ToString("D"), customerId, Now);
        await _methods.AddAsync(method, ct);

        GatewayVaultedCard vaulted;
        try
        {
            try
            {
                vaulted = await _gateway.VaultCardAsync(card, customerId, method.PayPalRequestId, ct);
            }
            catch (PaymentGatewayException ex) when (ex.OutcomeUnknown && !ex.BudgetExhausted)
            {
                // Settle in place: the card is still in memory, so re-send under the same PayPal-Request-Id.
                vaulted = await _gateway.VaultCardAsync(card, customerId, method.PayPalRequestId, ct);
            }
        }
        catch (PaymentGatewayException ex) when (ex.OutcomeUnknown)
        {
            method.SaveOutcomeUnknown(Now);
            await _methods.UpdateAsync(method, CancellationToken.None);
            throw new PaymentOutcomePendingException(
                "PayPal did not respond in time, so the card was not saved. Try again; a partially saved card is never usable and is cleaned up automatically.", ex);
        }
        catch (PaymentGatewayException ex)
        {
            _logger.LogWarning("Saving a card refused by PayPal: {Name} {Issue} (debug id {DebugId})",
                ex.ProviderErrorName ?? "-", ex.ProviderIssue ?? "-", ex.DebugId ?? "-");
            method.SaveFailed();
            await _methods.UpdateAsync(method, CancellationToken.None);
            throw;
        }

        method.Vaulted(vaulted.VaultId, vaulted.CustomerId, vaulted.Brand, vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);
        await _methods.UpdateAsync(method, CancellationToken.None);
        return method;
    }

    /// <summary>
    /// Removes the card for the shopper immediately (it disappears from the list and can no longer pay), then
    /// deletes the vault token at PayPal. If PayPal cannot confirm, the sweeper finishes the deletion.
    /// </summary>
    public async Task<SavedPaymentMethod> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var method = await _methods.FirstOrDefaultAsync(new SavedPaymentMethodForBuyerSpec(paymentMethodId, buyerId), ct);
        if (method is null || method.Status is SavedPaymentMethodStatus.Failed or SavedPaymentMethodStatus.Saving)
            throw PaymentRequestException.NotFound("Saved payment method");
        if (method.Status == SavedPaymentMethodStatus.Deleted)
            return method;

        method.Delete(Now);
        await _methods.UpdateAsync(method, ct);

        if (!await _claims.TryClaimAsync($"delete-card:{method.Id}", ct))
            return method; // another request is already removing the token

        await RemoveVaultTokenAsync(method, ct);
        return method;
    }

    /// <summary>Sweeper entry point for cards stuck in Saving or deleted locally but not yet at PayPal.</summary>
    public async Task SettleAsync(int paymentMethodId, CancellationToken ct)
    {
        var method = await _methods.GetByIdAsync(paymentMethodId, ct);
        if (method is null) return;

        if (method.Status == SavedPaymentMethodStatus.Deleted && !method.VaultTokenRemoved)
        {
            await RemoveVaultTokenAsync(method, ct);
            return;
        }

        if (method.Status != SavedPaymentMethodStatus.Saving) return;

        if (!string.IsNullOrEmpty(method.PayPalCustomerId))
        {
            // Any token PayPal holds for this customer that eShop does not consider active is an orphan of a lost save.
            var (vaultIds, complete) = await _gateway.ListVaultedCardsAsync(method.PayPalCustomerId, ct);
            if (!complete)
            {
                _logger.LogWarning("Vault listing for a customer was capped; leaving saved card {PaymentMethodId} unsettled.", method.Id);
                return;
            }

            var active = (await _methods.ListAsync(new SavedPaymentMethodsForBuyerSpec(method.BuyerId), ct))
                .Select(m => m.PayPalVaultId).ToHashSet();
            foreach (var orphan in vaultIds.Where(id => !active.Contains(id)))
            {
                await _gateway.DeleteVaultedCardAsync(orphan, ct);
            }
        }
        else
        {
            _logger.LogWarning("Saved card {PaymentMethodId} never confirmed and no PayPal customer is known to search; marking it failed.", method.Id);
        }

        method.SaveFailed();
        await _methods.UpdateAsync(method, ct);
    }

    private async Task RemoveVaultTokenAsync(SavedPaymentMethod method, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(method.PayPalVaultId))
        {
            method.VaultTokenDeleted();
            await _methods.UpdateAsync(method, CancellationToken.None);
            return;
        }

        try
        {
            await _gateway.DeleteVaultedCardAsync(method.PayPalVaultId, ct);
            method.VaultTokenDeleted();
        }
        catch (PaymentGatewayException ex) when (ex.Failure == PaymentGatewayFailure.NotFound)
        {
            method.VaultTokenDeleted();
        }
        catch (PaymentGatewayException ex)
        {
            // Settle: if PayPal no longer has the token, the delete landed (or it was already gone).
            try
            {
                if (!await _gateway.VaultedCardExistsAsync(method.PayPalVaultId, ct))
                    method.VaultTokenDeleted();
            }
            catch (PaymentGatewayException)
            {
                // Still unknown — left for the sweeper.
            }
            if (!method.VaultTokenRemoved)
                _logger.LogWarning("Vault token for saved card {PaymentMethodId} not yet deleted at PayPal ({Failure}); the sweeper will retry.", method.Id, ex.Failure);
        }

        await _methods.UpdateAsync(method, CancellationToken.None);
    }
}
