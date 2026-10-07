using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.DigitalFileEndpoints;

/// <summary>
/// Links a catalog item to a file in the merchant's Box folder
/// </summary>
public class LinkDigitalFileEndpoint : IEndpoint<IResult, LinkDigitalFileRequest, DigitalFileService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/catalog-items/{catalogItemId}/digital-file",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int catalogItemId, LinkDigitalFileRequest request, DigitalFileService digitalFileService, HttpContext httpContext) =>
            {
                request.CatalogItemId = catalogItemId;
                request.CancellationToken = httpContext.RequestAborted;
                return await HandleAsync(request, digitalFileService);
            })
            .Produces<LinkDigitalFileResponse>()
            .WithTags("DigitalFileEndpoints");
    }

    public async Task<IResult> HandleAsync(LinkDigitalFileRequest request, DigitalFileService digitalFileService)
    {
        var response = new LinkDigitalFileResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.FileId))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.FileId)] = new[] { "A Box file id is required." },
            });
        }

        LinkDigitalFileResult result;
        try
        {
            result = await digitalFileService.LinkAsync(request.CatalogItemId, request.FileId.Trim(), request.CancellationToken);
        }
        catch (DigitalFileStorageException ex)
        {
            return StorageProblem.From(ex);
        }

        switch (result.Status)
        {
            case LinkDigitalFileStatus.CatalogItemNotFound:
                return Results.Problem(statusCode: StatusCodes.Status404NotFound,
                    title: "Catalog item not found.",
                    detail: $"Catalog item {request.CatalogItemId} does not exist.");
            case LinkDigitalFileStatus.FileNotOffered:
                return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "File not found in Box.",
                    detail: result.ListingWasTruncated
                        ? $"File '{request.FileId}' was not found among the files listed from the digital products folder (the listing was truncated)."
                        : $"File '{request.FileId}' is not a file in the digital products folder.");
        }

        var link = result.Link!;
        response.CatalogItemId = link.CatalogItemId;
        response.FileId = link.FileId;
        response.FileName = link.FileName;
        response.Size = link.SizeInBytes;
        return Results.Ok(response);
    }
}
