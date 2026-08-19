using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

/// <summary>
/// Returns the status and outcome of one sync — whether it is still running, finished capturing the
/// whole listing, or finished capturing only part of it, plus how many products it found versus how
/// many it imported. Operator-only.
/// </summary>
public class GetSupplierSyncEndpoint : IEndpoint<IResult, GetSupplierSyncRequest, ISupplierCatalogSyncService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/catalog/syncs/{syncId:int}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int syncId, ISupplierCatalogSyncService service) =>
            {
                return await HandleAsync(new GetSupplierSyncRequest(syncId), service);
            })
            .Produces<GetSupplierSyncResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(GetSupplierSyncRequest request, ISupplierCatalogSyncService service)
    {
        var sync = await service.GetSyncAsync(request.SyncId);
        if (sync is null)
        {
            throw new SupplierSyncNotFoundException(request.SyncId);
        }

        var response = new GetSupplierSyncResponse(request.CorrelationId())
        {
            SyncId = sync.Id,
            SupplierId = sync.SupplierId,
            Status = sync.Status.ToString(),
            ItemsFound = sync.ItemsFound,
            ItemsImported = sync.ItemsImported,
            StatusDetail = sync.StatusDetail,
            CreatedAt = sync.CreatedAt,
            StartedAt = sync.StartedAt,
            CompletedAt = sync.CompletedAt
        };

        return Results.Ok(response);
    }
}
