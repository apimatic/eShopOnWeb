using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IShopperIdentityService
{
    Task<string> EnsureAsync(string shopperId, CancellationToken cancellationToken);
}

public sealed class ShopperIdentityService : IShopperIdentityService
{
    private readonly CatalogContext _db;
    private readonly IFinixGateway _gateway;
    private readonly PaymentClaimStore _claims;
    private readonly ILogger<ShopperIdentityService> _logger;

    public ShopperIdentityService(
        CatalogContext db,
        IFinixGateway gateway,
        PaymentClaimStore claims,
        ILogger<ShopperIdentityService> logger)
    {
        _db = db;
        _gateway = gateway;
        _claims = claims;
        _logger = logger;
    }

    public async Task<string> EnsureAsync(string shopperId, CancellationToken cancellationToken)
    {
        var existing = await _db.ShopperFinixIdentities.FindAsync(new object[] { shopperId }, cancellationToken);
        if (existing?.FinixIdentityId is { Length: > 0 } identityId)
        {
            return identityId;
        }

        using var gate = await _claims.AcquireAsync($"IDENTITY:{shopperId}", cancellationToken);

        existing = await _db.ShopperFinixIdentities.FindAsync(new object[] { shopperId }, cancellationToken);
        if (existing?.FinixIdentityId is { Length: > 0 } settledIdentityId)
        {
            return settledIdentityId;
        }

        var email = EmailFor(shopperId);

        if (existing is not null && existing.IsPending)
        {
            var settled = await _gateway.FindIdentityByEmailAsync(existing.Email, cancellationToken);
            if (settled is not null)
            {
                existing.Complete(settled.Id);
                await _db.SaveChangesAsync(cancellationToken);
                return settled.Id;
            }

            _db.ShopperFinixIdentities.Remove(existing);
            await _db.SaveChangesAsync(cancellationToken);
            existing = null;
        }

        var pending = existing ?? new ShopperFinixIdentity(shopperId, email);
        if (existing is null)
        {
            _db.ShopperFinixIdentities.Add(pending);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _db.Entry(pending).State = EntityState.Detached;
                var raced = await _db.ShopperFinixIdentities.FindAsync(new object[] { shopperId }, cancellationToken);
                if (raced?.FinixIdentityId is { Length: > 0 } racedId)
                {
                    return racedId;
                }

                throw new PaymentApiException(
                    409,
                    "A Finix identity is already being created for this shopper; retry shortly.",
                    "IDENTITY_IN_PROGRESS");
            }
        }

        try
        {
            var created = await _gateway.CreateIdentityAsync(pending.Email, cancellationToken);
            pending.Complete(created.Id);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Created Finix identity {IdentityId} for shopper {ShopperId}.", created.Id, shopperId);
            return created.Id;
        }
        catch (PaymentProviderException)
        {
            _db.ShopperFinixIdentities.Remove(pending);
            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private static string EmailFor(string shopperId) =>
        shopperId.Contains('@', StringComparison.Ordinal)
            ? shopperId
            : $"{shopperId.Replace(' ', '.').ToLowerInvariant()}@eshop.local";
}
