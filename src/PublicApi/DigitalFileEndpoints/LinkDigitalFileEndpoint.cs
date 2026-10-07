using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.DigitalFiles;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.DigitalFileEndpoints;

/// <summary>
/// Links a catalog item to a file in the merchant's digital products folder
/// </summary>
public class LinkDigitalFileEndpoint : IEndpoint<IResult, int, LinkDigitalFileRequest, DigitalFileService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/catalog-items/{catalogItemId:int}/digital-file",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int catalogItemId, LinkDigitalFileRequest request, DigitalFileService digitalFileService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(catalogItemId, request, digitalFileService, cancellationToken);
            })
            .Produces<LinkDigitalFileResponse>()
            .WithTags("DigitalFileEndpoints");
    }

    public Task<IResult> HandleAsync(int catalogItemId, LinkDigitalFileRequest request, DigitalFileService digitalFileService) =>
        HandleAsync(catalogItemId, request, digitalFileService, CancellationToken.None);

    public async Task<IResult> HandleAsync(int catalogItemId, LinkDigitalFileRequest request, DigitalFileService digitalFileService, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BoxFileId))
            return DigitalFileErrors.Error(StatusCodes.Status400BadRequest, "boxFileId is required.");

        var fileId = request.BoxFileId.Trim();
        DigitalFileLinkResult result;
        try
        {
            result = await digitalFileService.LinkAsync(catalogItemId, fileId, cancellationToken);
        }
        catch (DigitalFileProviderException ex)
        {
            return DigitalFileErrors.FromProvider(ex);
        }

        return result.Status switch
        {
            DigitalFileLinkStatus.Linked => Results.Ok(new LinkDigitalFileResponse(request.CorrelationId())
            {
                CatalogItemId = catalogItemId,
                BoxFileId = result.Link!.FileId,
                Name = result.Link.FileName,
                Size = result.Link.SizeBytes,
            }),
            DigitalFileLinkStatus.CatalogItemNotFound => DigitalFileErrors.Error(StatusCodes.Status404NotFound,
                $"Catalog item {catalogItemId} was not found."),
            DigitalFileLinkStatus.FileNotOffered => DigitalFileErrors.Error(StatusCodes.Status422UnprocessableEntity,
                $"Box file {fileId} is not in the digital products folder."),
            DigitalFileLinkStatus.FileUnreadable => DigitalFileErrors.Error(StatusCodes.Status422UnprocessableEntity,
                $"Box file {fileId} is in the digital products folder, but its details could not be read, so it cannot be linked."),
            _ => DigitalFileErrors.Error(StatusCodes.Status422UnprocessableEntity,
                $"Box file {fileId} was not found in the part of the digital products folder that could be listed."),
        };
    }
}
