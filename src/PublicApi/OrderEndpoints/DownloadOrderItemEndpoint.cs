using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.PublicApi.DigitalFileEndpoints;
using Microsoft.eShopWeb.PublicApi.DigitalFiles.Box;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Downloads the digital edition of an item the calling shopper bought
/// </summary>
public class DownloadOrderItemEndpoint : IEndpoint<IResult, DownloadOrderItemRequest, DigitalFileService>
{
    private readonly IDigitalFileStorage _storage;
    private readonly IOptionsMonitor<BoxOptions> _boxOptions;
    private readonly ILogger<DownloadOrderItemEndpoint> _logger;

    public DownloadOrderItemEndpoint(IDigitalFileStorage storage, IOptionsMonitor<BoxOptions> boxOptions,
        ILogger<DownloadOrderItemEndpoint> logger)
    {
        _storage = storage;
        _boxOptions = boxOptions;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/downloads/{catalogItemId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, int catalogItemId, DigitalFileService digitalFileService, ClaimsPrincipal user, HttpContext httpContext) =>
            {
                var request = new DownloadOrderItemRequest(orderId, catalogItemId, user.Identity?.Name ?? string.Empty,
                    httpContext.RequestAborted);
                return await HandleAsync(request, digitalFileService);
            })
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DownloadOrderItemRequest request, DigitalFileService digitalFileService)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var authorization = await digitalFileService.AuthorizeDownloadAsync(request.OrderId, request.CatalogItemId,
            request.BuyerId, request.CancellationToken);
        if (!authorization.IsAllowed)
        {
            _logger.LogWarning("Download refused ({Reason}): order {OrderId}, catalog item {CatalogItemId}.",
                authorization.DenialReason, request.OrderId, request.CatalogItemId);
            return authorization.DenialReason switch
            {
                DownloadDenialReason.ItemNotInOrder => NotFound("Item not in order.",
                    $"Catalog item {request.CatalogItemId} is not part of order {request.OrderId}."),
                DownloadDenialReason.NoDigitalFile => NotFound("No digital edition.",
                    $"Catalog item {request.CatalogItemId} has no downloadable file."),
                _ => NotFound("Order not found.", $"Order {request.OrderId} was not found."),
            };
        }

        var file = authorization.File!;
        DigitalFileContent content;
        try
        {
            content = await _storage.OpenReadAsync(file.FileId, request.CancellationToken);
        }
        catch (DigitalFileNotFoundException ex)
        {
            _logger.LogError(ex, "Linked file {FileId} of catalog item {CatalogItemId} no longer exists in Box.",
                file.FileId, request.CatalogItemId);
            return NotFound("File no longer available.",
                $"The file for catalog item {request.CatalogItemId} is no longer available.");
        }
        catch (DigitalFileStorageException ex)
        {
            return StorageProblem.From(ex);
        }

        _logger.LogInformation("Download started: order {OrderId}, catalog item {CatalogItemId}, file {FileId}.",
            request.OrderId, request.CatalogItemId, file.FileId);
        return new DigitalFileDownloadResult(content, content.FileName ?? file.FileName, _boxOptions.CurrentValue.StallTimeout,
            _logger, new DownloadLogContext(request.OrderId, request.CatalogItemId, file.FileId));
    }

    private static IResult NotFound(string title, string detail) =>
        Results.Problem(title: title, detail: detail, statusCode: StatusCodes.Status404NotFound);
}
