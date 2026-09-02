using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGatewayClient _gateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<SavedCard> savedCardRepository, IPaymentGatewayClient gateway, IAppLogger<SavedCardService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        // PayPal customer ids only allow a limited character set; derive a safe,
        // deterministic customer id from the buyer id.
        var customerId = ToPayPalCustomerId(buyerId);
        var vaulted = await _gateway.VaultCardAsync(card, customerId, $"eshop-vault-{Guid.NewGuid():N}", cancellationToken);

        var savedCard = new SavedCard(buyerId, vaulted.CustomerId, vaulted.PaymentTokenId,
            vaulted.Brand, vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);
        await _savedCardRepository.AddAsync(savedCard, cancellationToken);
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default)
    {
        var savedCard = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdSpecification(savedCardId), cancellationToken);
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            throw new SavedCardNotFoundException(savedCardId);
        }

        try
        {
            await _gateway.DeleteVaultedCardAsync(savedCard.PaymentTokenId, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone from the vault; still remove the local reference.
            _logger.LogInformation("Vault token for saved card {SavedCardId} was already deleted at PayPal.", savedCardId);
        }

        await _savedCardRepository.DeleteAsync(savedCard, cancellationToken);
    }

    private static string ToPayPalCustomerId(string buyerId)
    {
        var chars = buyerId.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        var id = new string(chars);
        return id.Length <= 64 ? id : id[..64];
    }
}
