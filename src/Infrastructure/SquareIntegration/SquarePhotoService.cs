using System;
using System.IO;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
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

public sealed record PhotoUploadResult(int CatalogItemId, string SquareItemId, string ImageId, string? ImageUrl);

/// <summary>Makes an uploaded JPEG/PNG the photo of an eShop item's Square item.</summary>
public sealed class SquarePhotoService
{
    private readonly CatalogContext _db;
    private readonly SquareClientHolder _clients;
    private readonly SquareMerchantContextProvider _merchantContext;
    private readonly SquareCatalogLocator _locator;
    private readonly SquareLeaseStore _leases;
    private readonly ILogger<SquarePhotoService> _logger;

    public SquarePhotoService(
        CatalogContext db,
        SquareClientHolder clients,
        SquareMerchantContextProvider merchantContext,
        SquareCatalogLocator locator,
        SquareLeaseStore leases,
        ILogger<SquarePhotoService> logger)
    {
        _db = db;
        _clients = clients;
        _merchantContext = merchantContext;
        _locator = locator;
        _leases = leases;
        _logger = logger;
    }

    /// <summary>
    /// Recognises JPEG and PNG by their file signature (not by the name or declared content type).
    /// </summary>
    public static SquareImageFormat? DetectFormat(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
        {
            return SquareImageFormat.Jpeg;
        }

        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (content.Length >= png.Length && content[..png.Length].SequenceEqual(png))
        {
            return SquareImageFormat.Png;
        }

        return null;
    }

    public async Task<PhotoUploadResult> UploadAsync(int catalogItemId, byte[] content, CancellationToken cancellationToken)
    {
        var format = DetectFormat(content)
                     ?? throw new SquareRequestException(415, "The photo must be a JPEG or PNG image.");
        if (content.Length > SquareConstants.MaxPhotoBytes)
        {
            throw new SquareRequestException(413, "The photo must be at most 5 MB.");
        }

        var catalogItem = await _db.CatalogItems.FindAsync([catalogItemId], cancellationToken).ConfigureAwait(false)
                          ?? throw new SquareRequestException(404, $"Catalog item {catalogItemId} does not exist.");

        var context = await _merchantContext.GetAsync(cancellationToken).ConfigureAwait(false);
        var lookup = await _locator.ResolveAsync(context.MerchantId, [catalogItemId], cancellationToken).ConfigureAwait(false);
        if (!lookup.Found.TryGetValue(catalogItemId, out var entry))
        {
            if (!lookup.IsComplete)
            {
                throw new SquareIntegrationException(SquareFailureKind.Unavailable,
                    "Could not determine whether the item exists in Square (catalog search was partial). Try again.");
            }

            throw new SquareRequestException(409,
                $"Catalog item {catalogItemId} is not in Square yet. Run POST /api/square/catalog/sync first.");
        }

        await using var lease = await _leases.TryAcquireAsync($"photo:{context.MerchantId}:{catalogItemId}", TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false)
            ?? throw new SquareOperationInProgressException($"A photo upload for catalog item {catalogItemId} is already in progress.");

        var image = await CreateImageAsync(entry.Item.Id, catalogItem.Name, catalogItemId, content, format, cancellationToken).ConfigureAwait(false);
        var imageUrl = image.ImageData?.Url;
        if (string.IsNullOrEmpty(imageUrl))
        {
            var objects = await _locator.RetrieveAsync([image.Id], cancellationToken).ConfigureAwait(false);
            imageUrl = objects.TryGetValue(image.Id, out var stored) ? stored.ImageData?.Url : null;
        }

        await _locator.RecordLinkAsync(context.MerchantId, catalogItemId, entry.Item.Id, entry.Variation.Id, image.Id, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Uploaded Square image {ImageId} for catalog item {CatalogItemId} (Square item {SquareItemId})",
            image.Id, catalogItemId, entry.Item.Id);
        return new PhotoUploadResult(catalogItemId, entry.Item.Id, image.Id, imageUrl);
    }

    private async Task<CatalogObject> CreateImageAsync(
        string squareItemId, string itemName, int catalogItemId, byte[] content, SquareImageFormat format, CancellationToken cancellationToken)
    {
        var idempotencyKey = Guid.NewGuid().ToString();
        var (mediaType, extension) = format == SquareImageFormat.Png ? ("image/png", "png") : ("image/jpeg", "jpg");

        // A fresh stream per attempt: the SDK never rewinds or re-reads a file part.
        CreateCatalogImageOperationRequest BuildRequest() => new()
        {
            Request = new CreateCatalogImageRequest
            {
                IdempotencyKey = idempotencyKey,
                ObjectId = squareItemId,
                IsPrimary = true,
                Image = new CatalogObject
                {
                    Type = CatalogObjectType.Image,
                    Id = $"#eshop-photo-{catalogItemId}",
                    ImageData = new CatalogImage { Name = itemName, Caption = itemName },
                },
            },
            ImageFile = new BinaryContent
            {
                Stream = new MemoryStream(content, writable: false),
                FileName = $"eshop-{catalogItemId}.{extension}",
                ContentType = new MediaTypeHeaderValue(mediaType),
            },
        };

        try
        {
            return await SendAsync(BuildRequest(), cancellationToken).ConfigureAwait(false);
        }
        catch (SquareIntegrationException ex) when (ex.MayHaveReachedSquare)
        {
            _logger.LogWarning("Photo upload outcome unknown ({Kind}); re-sending with the same idempotency key", ex.Kind);
            try
            {
                return await SendAsync(BuildRequest(), cancellationToken).ConfigureAwait(false);
            }
            catch (SquareIntegrationException retry) when (retry.MayHaveReachedSquare)
            {
                throw new SquareIntegrationException(SquareFailureKind.OutcomeUnknown,
                    "Square did not confirm the photo upload. Check the item in Square before uploading again.",
                    retry.ProviderStatus, retry.ErrorCodes, retry);
            }
        }
    }

    private async Task<CatalogObject> SendAsync(CreateCatalogImageOperationRequest request, CancellationToken cancellationToken)
    {
        using var file = request.ImageFile;
        var response = await SquareCall.RunAsync("Catalog.CreateCatalogImage",
            ct => _clients.Client.Catalog.CreateCatalogImage(request, cancellationToken: ct), _logger, cancellationToken).ConfigureAwait(false);
        return response.Image is { Id.Length: > 0 } image
            ? image
            : throw new SquareIntegrationException(SquareFailureKind.UnreadableResponse, "Square did not return the created image.");
    }
}
