using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

/// <summary>
/// Reports the status and outcome of one sync: whether it is still running, finished capturing the
/// whole listing, or finished capturing only part of it, plus how many products it found versus
/// imported. Operator-only.
/// </summary>
public class GetSupplierSyncEndpoint : IEndpoint<IResult, GetSupplierSyncRequest, ISupplierSyncService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/catalog/syncs/{syncId}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int syncId, ISupplierSyncService supplierSyncService) =>
            {
                return await HandleAsync(new GetSupplierSyncRequest(syncId), supplierSyncService);
            })
            .Produces<GetSupplierSyncResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(GetSupplierSyncRequest request, ISupplierSyncService supplierSyncService)
    {
        var sync = await supplierSyncService.GetSyncAsync(request.SyncId);
        if (sync is null)
        {
            return Results.NotFound($"Sync {request.SyncId} was not found.");
        }

        var response = new GetSupplierSyncResponse(request.CorrelationId())
        {
            SyncId = sync.Id,
            SupplierId = sync.SupplierId,
            Status = sync.Status.ToString(),
            ItemsFound = sync.ItemsFound,
            ItemsImported = sync.ItemsImported,
            ErrorMessage = sync.ErrorMessage,
            CreatedAt = sync.CreatedAt,
            StartedAt = sync.StartedAt,
            CompletedAt = sync.CompletedAt
        };

        return Results.Ok(response);
    }
}
