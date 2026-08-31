using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentMethodService
{
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;

    public PaymentMethodService(CatalogContext db, IPayPalClient payPal)
    {
        _db = db;
        _payPal = payPal;
    }

    public async Task<PaymentMethodDto> CreateAsync(string buyerId, SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Card is null)
            throw new PaymentApiException(400, "CARD_REQUIRED", "card is required.");
        PaymentService.ValidateCard(request.Card);

        PayPalVaultResult remote;
        try
        {
            remote = await _payPal.CreatePaymentTokenAsync(buyerId, request.Card, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.RequiresPayerAction)
        {
            throw new PaymentApiException(409, "PAYPAL_PAYER_ACTION_REQUIRED",
                "PayPal requires a browser challenge for this card. This direct-card API flow cannot continue.");
        }
        catch (PayPalApiException ex)
        {
            throw ToApiException(ex);
        }

        var existing = await _db.SavedPaymentMethods.SingleOrDefaultAsync(x =>
            x.PayPalTokenId == remote.Id && x.BuyerId == buyerId, cancellationToken);
        if (existing is not null) return ToDto(existing);

        var saved = new SavedPaymentMethod(buyerId, remote.Id, remote.Brand, remote.LastDigits, remote.Expiry);
        _db.SavedPaymentMethods.Add(saved);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(saved);
    }

    public async Task<IReadOnlyList<PaymentMethodDto>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        var methods = await _db.SavedPaymentMethods.AsNoTracking().Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
        return methods.Select(ToDto).ToList();
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var saved = await _db.SavedPaymentMethods.SingleOrDefaultAsync(
            x => x.Id == paymentMethodId && x.BuyerId == buyerId, cancellationToken);
        if (saved is null)
            throw new PaymentApiException(404, "PAYMENT_METHOD_NOT_FOUND",
                "The saved card does not exist or does not belong to this shopper.");

        try
        {
            await _payPal.DeletePaymentTokenAsync(saved.PayPalTokenId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // The desired state already exists remotely; complete the local deletion.
        }
        catch (PayPalApiException ex)
        {
            throw ToApiException(ex);
        }

        _db.SavedPaymentMethods.Remove(saved);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static PaymentMethodDto ToDto(SavedPaymentMethod method) =>
        new(method.Id, method.Brand, method.LastDigits, method.Expiry, method.CreatedAt);

    private static PaymentApiException ToApiException(PayPalApiException ex)
    {
        var suffix = ex.DebugId is null ? string.Empty : $" PayPal debug ID: {ex.DebugId}.";
        var status = ex.StatusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
            ? (int)ex.StatusCode : 502;
        return new PaymentApiException(status, ex.Issue ?? ex.Name ?? "PAYPAL_ERROR", ex.Message + suffix);
    }
}

public sealed record SavePaymentMethodRequest(CardInput Card);
public sealed record PaymentMethodDto(int PaymentMethodId, string Brand, string LastDigits, string Expiry,
    DateTimeOffset CreatedAt);
