using System.Threading.Tasks;
using BlazorShared.Authorization;
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
/// Reports the status and outcome of one sync: whether it is still running, finished having captured
/// the whole listing, or finished having captured only part of it, plus how many products it found
/// versus imported. Operator-only.
/// </summary>
public class GetSyncStatusEndpoint : IEndpoint<IResult, GetSyncStatusRequest, IRepository<CatalogSync>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/catalog/syncs/{syncId}",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int syncId, IRepository<CatalogSync> syncRepository) =>
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
            return Results.NotFound($"No sync with id {request.SyncId} exists.");
        }

        var response = new GetSyncStatusResponse(request.CorrelationId())
        {
            SyncId = sync.Id,
            SupplierId = sync.SupplierId,
            Status = sync.Status.ToString(),
            ItemsFound = sync.ItemsFound,
            ItemsImported = sync.ItemsImported,
            ExternalJobId = sync.ExternalJobId,
            ErrorMessage = sync.ErrorMessage,
            CreatedDate = sync.CreatedDate,
            StartedDate = sync.StartedDate,
            CompletedDate = sync.CompletedDate
        };

        return Results.Ok(response);
    }
}
