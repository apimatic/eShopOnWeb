using System;
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
/// Reports the status and outcome of a single sync: whether it is still running, finished
/// capturing the whole listing, or finished capturing only part of it, plus how many products
/// were found versus imported. Administrator-only.
/// </summary>
public class GetSyncStatusEndpoint : IEndpoint<IResult, GetSyncStatusRequest, IRepository<CatalogSync>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/catalog/syncs/{syncId:guid}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (Guid syncId, IRepository<CatalogSync> syncRepository) =>
            {
                return await HandleAsync(new GetSyncStatusRequest(syncId), syncRepository);
            })
            .Produces<GetSyncStatusResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(GetSyncStatusRequest request, IRepository<CatalogSync> syncRepository)
    {
        var sync = await syncRepository.GetByIdAsync(request.SyncId);
        if (sync is null)
        {
            return Results.NotFound($"Sync {request.SyncId} was not found.");
        }

        var response = new GetSyncStatusResponse(request.CorrelationId())
        {
            SyncId = sync.Id,
            SupplierId = sync.SupplierId,
            Status = sync.Status.ToString(),
            ItemsFound = sync.ItemsFound,
            ItemsImported = sync.ItemsImported,
            CreatedAt = sync.CreatedAt,
            StartedAt = sync.StartedAt,
            CompletedAt = sync.CompletedAt,
            Detail = sync.Detail
        };

        return Results.Ok(response);
    }
}
