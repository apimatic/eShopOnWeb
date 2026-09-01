using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalClient _payPalClient;
    private readonly IAppLogger<SavedPaymentMethodService> _logger;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalClient payPalClient,
        IAppLogger<SavedPaymentMethodService> logger)
    {
        _repository = repository;
        _payPalClient = payPalClient;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCard card,
        CancellationToken cancellationToken = default)
    {
        var requestId = $"eshop-vault-{Guid.NewGuid():N}";
        var token = await _payPalClient.CreateCardPaymentTokenAsync(card, buyerId, requestId, cancellationToken);

        var lastDigits = token.LastDigits;
        if (string.IsNullOrWhiteSpace(lastDigits) && card.Number.Length >= 4)
        {
            lastDigits = card.Number[^4..];
        }

        var saved = new SavedPaymentMethod(buyerId, token.Id,
            token.Brand ?? "UNKNOWN", lastDigits ?? string.Empty,
            token.Expiry ?? card.Expiry, card.Name);
        await _repository.AddAsync(saved, cancellationToken);

        _logger.LogInformation("Saved card ending in {LastDigits} for buyer", lastDigits ?? "????");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpec(paymentMethodId), cancellationToken);
        if (saved is null || saved.BuyerId != buyerId)
        {
            throw new NotFoundException($"Saved payment method {paymentMethodId} was not found.");
        }

        try
        {
            await _payPalClient.DeletePaymentTokenAsync(saved.VaultTokenId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone from PayPal's vault; still remove the local record.
        }

        await _repository.DeleteAsync(saved, cancellationToken);
        _logger.LogInformation("Deleted saved payment method {PaymentMethodId} for buyer", paymentMethodId);
    }
}
