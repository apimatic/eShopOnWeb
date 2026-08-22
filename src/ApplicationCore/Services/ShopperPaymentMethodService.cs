using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperPaymentMethodService : IShopperPaymentMethodService
{
    private readonly IRepository<ShopperPaymentMethod> _repository;
    private readonly IPayPalGateway _payPal;

    public ShopperPaymentMethodService(
        IRepository<ShopperPaymentMethod> repository,
        IPayPalGateway payPal)
    {
        _repository = repository;
        _payPal = payPal;
    }

    public async Task<ShopperPaymentMethod> SaveCardAsync(
        string buyerId,
        CardPaymentRequest card,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var payPalCard = OrderPaymentService.ToPayPalCard(card);
        var customerId = CreatePayPalCustomerId(buyerId);
        var last4 = payPalCard.Number.Length >= 4 ? payPalCard.Number[^4..] : payPalCard.Number;
        var requestId = OrderPaymentService.TruncateRequestId(
            $"eshop-vault-{customerId}-{last4}-{payPalCard.Expiry}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");

        var vaulted = await _payPal.VaultCardAsync(new PayPalVaultCardCommand
        {
            PayPalCustomerId = customerId,
            RequestId = requestId,
            Card = payPalCard
        }, cancellationToken);

        var lastDigits = string.IsNullOrWhiteSpace(vaulted.LastDigits) ? last4 : vaulted.LastDigits;
        var entity = new ShopperPaymentMethod(
            buyerId,
            vaulted.PayPalCustomerId ?? customerId,
            vaulted.VaultId,
            lastDigits,
            vaulted.Brand ?? string.Empty,
            vaulted.Expiry ?? payPalCard.Expiry,
            vaulted.CardholderName ?? payPalCard.Name);

        return await _repository.AddAsync(entity, cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperPaymentMethod>> ListAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new ShopperPaymentMethodsSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var method = await _repository.FirstOrDefaultAsync(
            new ShopperPaymentMethodByIdSpecification(paymentMethodId, buyerId),
            cancellationToken);
        if (method is null)
        {
            throw new EntityNotFoundException($"Payment method {paymentMethodId} was not found.");
        }

        try
        {
            await _payPal.DeleteVaultedCardAsync(method.PayPalVaultId, cancellationToken);
        }
        catch (PayPalGatewayException ex) when (ex.HttpStatus == 404)
        {
            // Already removed at PayPal; still drop the local record.
        }

        await _repository.DeleteAsync(method, cancellationToken);
    }

    /// <summary>
    /// Vault create-token customer.id is max 22 chars per the Payment Method Tokens spec.
    /// </summary>
    public static string CreatePayPalCustomerId(string buyerId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buyerId))).ToLowerInvariant();
        return hash[..22];
    }
}
