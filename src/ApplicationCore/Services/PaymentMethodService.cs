using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Saves and manages shopper cards via PayPal's vault. The application keeps only the vault
/// token and a safe descriptor — never full card details — and every action is scoped to the
/// caller's own cards.
/// </summary>
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

    public async Task<Result<PaymentMethod>> SaveCardAsync(string buyerId, CardDetails card,
        CancellationToken cancellationToken = default)
    {
        if (card is null || string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
            return Result<PaymentMethod>.Invalid(new List<ValidationError> { new ValidationError { ErrorMessage = "Card number and expiry are required." } });

        VaultCardResult vaulted;
        try
        {
            vaulted = await _gateway.VaultCardAsync(card, customerId: null,
                idempotencyKey: Guid.NewGuid().ToString("N"), cancellationToken);
        }
        catch (PaymentException ex)
        {
            _logger.LogWarning($"Vaulting card failed for {buyerId}: {ex.Message}");
            return Result<PaymentMethod>.Error($"The card could not be saved ({ex.Message}).");
        }

        if (vaulted.RequiresBrowserApproval)
        {
            return Result<PaymentMethod>.Error(
                "PayPal returned a challenge that requires the shopper to approve saving this card in a " +
                "browser (3-D Secure). This integration saves cards without a browser step and cannot " +
                "complete this. Ask the shopper to use a different card.");
        }

        var method = new PaymentMethod(buyerId, vaulted.VaultId, vaulted.CustomerId,
            vaulted.Brand, vaulted.LastFourDigits, vaulted.Expiry, card.CardholderName);
        await _repository.AddAsync(method, cancellationToken);

        _logger.LogInformation($"Saved card {vaulted.Brand} ending {vaulted.LastFourDigits} for {buyerId} (id {method.Id}).");
        return Result<PaymentMethod>.Success(method);
    }

    public async Task<IReadOnlyCollection<PaymentMethod>> GetCardsForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new PaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<Result> DeleteCardAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var method = await _repository.FirstOrDefaultAsync(
            new PaymentMethodByIdSpecification(paymentMethodId, buyerId), cancellationToken);
        if (method is null)
            return Result.NotFound();

        // Best-effort removal at PayPal; the local delete below guarantees it can no longer pay
        // through this app even if the remote call fails.
        try
        {
            await _gateway.DeleteVaultTokenAsync(method.VaultId, cancellationToken);
        }
        catch (PaymentException ex)
        {
            _logger.LogWarning($"Deleting vault token at PayPal failed for card {paymentMethodId}: {ex.Message}");
        }

        await _repository.DeleteAsync(method, cancellationToken);
        _logger.LogInformation($"Deleted saved card {paymentMethodId} for {buyerId}.");
        return Result.Success();
    }
}
