using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

/// <summary>
/// Reports the status and outcome of a single sync: whether it is still running, finished having
/// captured the whole listing, or finished having captured only part of it, plus how many products
/// were found versus imported. Operator-only.
/// </summary>
public class GetSupplierSyncEndpoint : IEndpoint<IResult, int, IReadRepository<SupplierSync>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/catalog/syncs/{syncId}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int syncId, IReadRepository<SupplierSync> syncRepository) =>
            {
                return await HandleAsync(syncId, syncRepository);
            })
            .Produces<GetSupplierSyncResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(int syncId, IReadRepository<SupplierSync> syncRepository)
    {
        var sync = await syncRepository.GetByIdAsync(syncId);
        if (sync is null)
            return Results.NotFound($"Sync {syncId} was not found.");

        var response = new GetSupplierSyncResponse
        {
            SyncId = sync.Id,
            SupplierId = sync.SupplierId,
            Status = sync.Status.ToString(),
            ItemsFound = sync.ItemsFound,
            ItemsImported = sync.ItemsImported,
            CreatedAt = sync.CreatedAt,
            StartedAt = sync.StartedAt,
            CompletedAt = sync.CompletedAt,
            Error = sync.Error
        };

        return Results.Ok(response);
    }
}
