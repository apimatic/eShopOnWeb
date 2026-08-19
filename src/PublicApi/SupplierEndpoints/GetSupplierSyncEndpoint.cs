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
/// Returns the status and outcome of one supplier sync — whether it is still running, finished
/// having captured the whole listing or only part of it, and how many products it found versus
/// imported. Operator-only.
/// </summary>
public class GetSupplierSyncEndpoint : IEndpoint<IResult, GetSupplierSyncRequest, IReadRepository<SupplierSync>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/catalog/syncs/{syncId}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int syncId, IReadRepository<SupplierSync> syncRepository) =>
            {
                return await HandleAsync(new GetSupplierSyncRequest(syncId), syncRepository);
            })
            .Produces<GetSupplierSyncResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(GetSupplierSyncRequest request, IReadRepository<SupplierSync> syncRepository)
    {
        var sync = await syncRepository.GetByIdAsync(request.SyncId);
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
            CreatedAt = sync.CreatedAt,
            StartedAt = sync.StartedAt,
            CompletedAt = sync.CompletedAt,
            ErrorMessage = sync.ErrorMessage
        };

        return Results.Ok(response);
    }
}
