using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.DigitalFileEndpoints;

/// <summary>
/// Lists the files in the merchant's Box folder that can be linked to catalog items
/// </summary>
public class ListDigitalFilesEndpoint : IEndpoint<IResult, IDigitalFileStorage>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/digital-files",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IDigitalFileStorage storage, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(storage, cancellationToken);
            })
            .Produces<ListDigitalFilesResponse>()
            .WithTags("DigitalFileEndpoints");
    }

    public Task<IResult> HandleAsync(IDigitalFileStorage storage) => HandleAsync(storage, CancellationToken.None);

    public async Task<IResult> HandleAsync(IDigitalFileStorage storage, CancellationToken cancellationToken)
    {
        var response = new ListDigitalFilesResponse();

        DigitalFileListing listing;
        try
        {
            listing = await storage.ListFilesAsync(cancellationToken);
        }
        catch (DigitalFileStorageException ex)
        {
            return StorageProblem.From(ex);
        }

        response.FolderName = listing.FolderName;
        response.IsTruncated = listing.IsTruncated;
        response.Files.AddRange(listing.Files.Select(file => new DigitalFileDto
        {
            Id = file.Id,
            Name = file.Name,
            Size = file.SizeInBytes,
            Sha1 = file.Sha1,
        }));

        return Results.Ok(response);
    }
}
