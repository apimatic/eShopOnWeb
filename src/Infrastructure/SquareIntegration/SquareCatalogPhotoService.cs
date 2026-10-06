using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration.Entities;
using Microsoft.Extensions.Logging;
using Square.Core.Exceptions;
using Square.Core.Models;
using Square.Models;
using Square.Models.Enums;
using Square.Requests.Catalog;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

public enum SquareImageFormat
{
    Jpeg,
    Png,
}

public static class SquareImageValidation
{
    /// <summary>Largest photo accepted (5 MB).</summary>
    public const long MaxBytes = 5L * 1024 * 1024;

    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Identifies JPEG and PNG by their file signature; whatever the client claims the type is does not count.</summary>
    public static SquareImageFormat? Detect(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(JpegSignature)) return SquareImageFormat.Jpeg;
        if (content.StartsWith(PngSignature)) return SquareImageFormat.Png;
        return null;
    }
}

public sealed record SquarePhotoResult(string ImageId, string? ImageUrl, string SquareItemId);

/// <summary>
/// Uploads a product photo as the primary image of the product's Square item (creating the Square item first
/// when the catalog was not synced yet).
/// </summary>
public sealed class SquareCatalogPhotoService
{
    private static readonly TimeSpan StaleClaim = TimeSpan.FromMinutes(2);

    private readonly CatalogContext _db;
    private readonly SquareCatalogSync _sync;
    private readonly SquareClientProvider _clients;
    private readonly SquareMerchantContextProvider _merchants;
    private readonly SquareCatalogWriteGate _gate;
    private readonly TimeProvider _clock;
    private readonly ILogger<SquareCatalogPhotoService> _logger;

    public SquareCatalogPhotoService(CatalogContext db, SquareCatalogSync sync, SquareClientProvider clients,
        SquareMerchantContextProvider merchants, SquareCatalogWriteGate gate, TimeProvider clock,
        ILogger<SquareCatalogPhotoService> logger)
    {
        _db = db;
        _sync = sync;
        _clients = clients;
        _merchants = merchants;
        _gate = gate;
        _clock = clock;
        _logger = logger;
    }

    /// <returns>The uploaded image, or null when the eShop catalog item does not exist.</returns>
    public async Task<SquarePhotoResult?> UploadAsync(int catalogItemId, byte[] image, SquareImageFormat format,
        CancellationToken cancellationToken)
    {
        var item = await _db.CatalogItems.AsNoTracking().SingleOrDefaultAsync(i => i.Id == catalogItemId, cancellationToken);
        if (item is null) return null;

        var merchant = await _merchants.GetAsync(cancellationToken);
        using var _ = await _gate.EnterAsync(cancellationToken);

        var link = await _sync.EnsureItemSyncedAsync(item, merchant, cancellationToken);
        var squareItemId = link.SquareItemId!;
        var hash = Convert.ToHexString(SHA256.HashData(image));

        var squareItem = await _sync.RetrieveObjectAsync(squareItemId, cancellationToken)
            ?? throw new SquareIntegrationException(SquareFailureKind.Conflict, "The Square item disappeared; run the catalog sync and retry.");
        var primaryBefore = squareItem.ItemData?.ImageIds?.FirstOrDefault();

        // The same photo is already this item's photo: a repeated PUT changes nothing.
        if (link.PhotoState == SquarePhotoState.Done && link.PhotoSha256 == hash && link.PhotoImageId is not null
            && link.PhotoImageId == primaryBefore)
        {
            return new SquarePhotoResult(link.PhotoImageId, link.PhotoImageUrl, squareItemId);
        }

        var previous = (link.PhotoState, link.PhotoSha256, link.PhotoIdempotencyKey, link.PhotoClaimedAt);
        if (!await TryClaimAsync(link, hash, cancellationToken))
            throw new SquareIntegrationException(SquareFailureKind.Conflict,
                "Another photo upload for this product is in progress; try again shortly.");

        CreateCatalogImageResponse response;
        try
        {
            response = await _clients.Merchant.Catalog.CreateCatalogImage(new CreateCatalogImageOperationRequest
            {
                Request = new CreateCatalogImageRequest
                {
                    IdempotencyKey = link.PhotoIdempotencyKey!,
                    ObjectId = squareItemId,
                    IsPrimary = true,
                    Image = new CatalogObject
                    {
                        Type = CatalogObjectType.Image,
                        Id = $"#eshop-image-{item.Id}",
                        ImageData = new CatalogImage { Name = item.Name, Caption = item.Name },
                    },
                },
                ImageFile = new BinaryContent
                {
                    Stream = new MemoryStream(image, writable: false),
                    FileName = $"catalog-item-{item.Id}.{(format == SquareImageFormat.Png ? "png" : "jpg")}",
                    ContentType = new MediaTypeHeaderValue(format == SquareImageFormat.Png ? "image/png" : "image/jpeg"),
                },
            }, cancellationToken: cancellationToken);
        }
        catch (SdkConnectionException ex)
        {
            // The upload may have landed. It did if the item's primary image changed.
            _logger.LogWarning(ex, "Square image upload for item {CatalogItemId} has an unknown outcome; reading the item to settle it.", item.Id);
            var settled = await SettleFromItemAsync(squareItemId, primaryBefore, cancellationToken);
            if (settled is null)
                throw new SquareIntegrationException(SquareFailureKind.OutcomeUnknown,
                    "Square did not confirm the photo upload. Upload the same photo again to finish it.", innerException: ex);
            return await CompleteAsync(link, settled, squareItemId, cancellationToken);
        }
        catch (SdkException ex)
        {
            var failure = SquareErrors.Translate(ex, "the Square image upload", isWrite: true);
            if (failure.Kind != SquareFailureKind.OutcomeUnknown)
            {
                // Square refused it: release the claim so the next upload is not blocked.
                (link.PhotoState, link.PhotoSha256, link.PhotoIdempotencyKey, link.PhotoClaimedAt) = previous;
                link.ConcurrencyStamp = Guid.NewGuid();
                await _db.SaveChangesAsync(CancellationToken.None);
            }
            throw failure;
        }

        var uploaded = response.Image
            ?? throw new SquareIntegrationException(SquareFailureKind.OutcomeUnknown, "Square returned no image for the upload.");
        if (uploaded.ImageData?.Url is null)
            uploaded = await _sync.RetrieveObjectAsync(uploaded.Id, cancellationToken) ?? uploaded;
        return await CompleteAsync(link, uploaded, squareItemId, cancellationToken);
    }

    private async Task<bool> TryClaimAsync(SquareCatalogLink link, string hash, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var pending = link.PhotoState == SquarePhotoState.Pending;
        if (pending && link.PhotoSha256 != hash && link.PhotoClaimedAt is { } claimedAt && now - claimedAt < StaleClaim)
            return false;

        // Re-sending the same photo after an unknown outcome reuses its key so Square can recognise the retry.
        if (!(pending && link.PhotoSha256 == hash && link.PhotoIdempotencyKey is not null))
            link.PhotoIdempotencyKey = Guid.NewGuid().ToString();
        link.PhotoState = SquarePhotoState.Pending;
        link.PhotoSha256 = hash;
        link.PhotoClaimedAt = now;
        link.UpdatedAt = now;
        link.ConcurrencyStamp = Guid.NewGuid();
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await _db.Entry(link).ReloadAsync(cancellationToken);
            return false;
        }
    }

    private async Task<CatalogObject?> SettleFromItemAsync(string squareItemId, string? primaryBefore, CancellationToken cancellationToken)
    {
        try
        {
            var item = await _sync.RetrieveObjectAsync(squareItemId, cancellationToken);
            var primaryNow = item?.ItemData?.ImageIds?.FirstOrDefault();
            if (primaryNow is null || primaryNow == primaryBefore) return null;
            return await _sync.RetrieveObjectAsync(primaryNow, cancellationToken);
        }
        catch (SquareIntegrationException)
        {
            return null;
        }
    }

    private async Task<SquarePhotoResult> CompleteAsync(SquareCatalogLink link, CatalogObject image, string squareItemId,
        CancellationToken cancellationToken)
    {
        link.PhotoState = SquarePhotoState.Done;
        link.PhotoImageId = image.Id;
        link.PhotoImageUrl = image.ImageData?.Url;
        link.PhotoIdempotencyKey = null;
        link.PhotoClaimedAt = null;
        link.UpdatedAt = _clock.GetUtcNow();
        link.ConcurrencyStamp = Guid.NewGuid();
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Uploaded Square image {ImageId} for catalog item {CatalogItemId}.", image.Id, link.CatalogItemId);
        return new SquarePhotoResult(image.Id, link.PhotoImageUrl, squareItemId);
    }
}
