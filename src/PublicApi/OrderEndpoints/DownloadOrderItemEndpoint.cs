using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.DigitalFiles;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.PublicApi.DigitalFileEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Downloads the digital file of an item in one of the signed-in user's orders
/// </summary>
public class DownloadOrderItemEndpoint : IEndpoint<IResult, DownloadOrderItemRequest, DigitalFileService, IDigitalFileProvider>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/downloads/{catalogItemId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, int catalogItemId, ClaimsPrincipal user, DigitalFileService digitalFileService, IDigitalFileProvider provider, CancellationToken cancellationToken) =>
            {
                var request = new DownloadOrderItemRequest(orderId, catalogItemId, user.Identity?.Name);
                return await HandleAsync(request, digitalFileService, provider, cancellationToken);
            })
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(DownloadOrderItemRequest request, DigitalFileService digitalFileService, IDigitalFileProvider provider) =>
        HandleAsync(request, digitalFileService, provider, CancellationToken.None);

    public async Task<IResult> HandleAsync(DownloadOrderItemRequest request, DigitalFileService digitalFileService,
        IDigitalFileProvider provider, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        var authorization = await digitalFileService.AuthorizeDownloadAsync(request.OrderId, request.CatalogItemId, request.BuyerId, cancellationToken);
        switch (authorization.Status)
        {
            case DigitalDownloadStatus.OrderNotFound:
                return DigitalFileErrors.Error(StatusCodes.Status404NotFound, $"Order {request.OrderId} was not found.");
            case DigitalDownloadStatus.ItemNotInOrder:
                return DigitalFileErrors.Error(StatusCodes.Status404NotFound,
                    $"Catalog item {request.CatalogItemId} is not part of order {request.OrderId}.");
            case DigitalDownloadStatus.NoDigitalFile:
                return DigitalFileErrors.Error(StatusCodes.Status404NotFound,
                    $"Catalog item {request.CatalogItemId} has no digital file to download.");
        }

        var link = authorization.Link!;
        DigitalFileContent content;
        try
        {
            content = await provider.OpenReadAsync(link.FileId, cancellationToken);
        }
        catch (DigitalFileProviderException ex) when (ex.Failure == DigitalFileProviderFailure.NotFound)
        {
            return DigitalFileErrors.Error(StatusCodes.Status502BadGateway,
                "The file for this item is no longer available in storage. Please contact the shop.");
        }
        catch (DigitalFileProviderException ex)
        {
            return DigitalFileErrors.FromProvider(ex);
        }

        return new DigitalFileDownloadResult(content, link.FileName,
            new DownloadLogContext(request.OrderId, request.CatalogItemId, link.FileId));
    }
}

public sealed record DownloadOrderItemRequest(int OrderId, int CatalogItemId, string? BuyerId);
