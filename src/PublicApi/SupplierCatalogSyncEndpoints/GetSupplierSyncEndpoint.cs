using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierCatalogAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SupplierCatalogSyncEndpoints;

/// <summary>
/// Returns the status and outcome of a sync. Operator-only.
/// </summary>
public class GetSupplierSyncEndpoint : IEndpoint<IResult, GetSupplierSyncRequest, IReadRepository<CatalogSync>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/catalog/syncs/{syncId:guid}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (Guid syncId, IReadRepository<CatalogSync> syncRepository) =>
            {
                return await HandleAsync(new GetSupplierSyncRequest(syncId), syncRepository);
            })
            .Produces<GetSupplierSyncResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SupplierCatalogSyncEndpoints");
    }

    public async Task<IResult> HandleAsync(GetSupplierSyncRequest request, IReadRepository<CatalogSync> syncRepository)
    {
        var sync = await syncRepository.GetByIdAsync(request.SyncId);
        if (sync is null)
            return Results.NotFound($"Sync {request.SyncId} was not found.");

        var response = new GetSupplierSyncResponse
        {
            SyncId = sync.Id,
            SupplierId = sync.SupplierId,
            Status = sync.Status.ToString(),
            ItemsFound = sync.ItemsFound,
            ItemsImported = sync.ItemsImported,
            Error = sync.Error,
            CreatedAt = sync.CreatedAt,
            StartedAt = sync.StartedAt,
            CompletedAt = sync.CompletedAt
        };

        return Results.Ok(response);
    }
}
