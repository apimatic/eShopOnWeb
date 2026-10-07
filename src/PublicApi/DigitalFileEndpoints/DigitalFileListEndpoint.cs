using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.DigitalFiles;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.DigitalFileEndpoints;

/// <summary>
/// Lists the files in the merchant's digital products folder so an operator can link one to a catalog item
/// </summary>
public class DigitalFileListEndpoint : IEndpoint<IResult, IDigitalFileProvider>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/digital-files",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IDigitalFileProvider provider, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(provider, cancellationToken);
            })
            .Produces<ListDigitalFilesResponse>()
            .WithTags("DigitalFileEndpoints");
    }

    public Task<IResult> HandleAsync(IDigitalFileProvider provider) => HandleAsync(provider, CancellationToken.None);

    public async Task<IResult> HandleAsync(IDigitalFileProvider provider, CancellationToken cancellationToken)
    {
        var response = new ListDigitalFilesResponse();

        DigitalFileListing listing;
        try
        {
            listing = await provider.ListFilesAsync(cancellationToken);
        }
        catch (DigitalFileProviderException ex)
        {
            return DigitalFileErrors.FromProvider(ex);
        }

        response.DigitalFiles = listing.Files
            .Select(f => new DigitalFileDto { BoxFileId = f.Id, Name = f.Name, Size = f.SizeBytes })
            .ToList();
        response.Complete = listing.IsComplete;
        response.UnreadableEntries = listing.UnreadableEntries
            .Select(e => new UnreadableDigitalFileDto { BoxFileId = e.Id, Name = e.Name, Reason = e.Reason })
            .ToList();
        return Results.Ok(response);
    }
}
