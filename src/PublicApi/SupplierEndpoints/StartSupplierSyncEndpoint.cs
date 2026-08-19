using System;
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
/// Starts a sync of a supplier's product listing. Returns immediately with the sync id; the
/// listing is read and imported in the background. Administrator-only.
/// </summary>
public class StartSupplierSyncEndpoint : IEndpoint<IResult, StartSupplierSyncRequest, ISupplierSyncStarter>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/catalog/suppliers/{supplierId:guid}/sync",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (Guid supplierId, ISupplierSyncStarter starter) =>
            {
                return await HandleAsync(new StartSupplierSyncRequest(supplierId), starter);
            })
            .Produces<StartSupplierSyncResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(StartSupplierSyncRequest request, ISupplierSyncStarter starter)
    {
        var sync = await starter.StartAsync(request.SupplierId);
        if (sync is null)
        {
            return Results.NotFound($"Supplier {request.SupplierId} was not found.");
        }

        var response = new StartSupplierSyncResponse(request.CorrelationId())
        {
            SyncId = sync.Id,
            SupplierId = sync.SupplierId,
            Status = sync.Status.ToString()
        };

        return Results.Accepted($"api/catalog/syncs/{sync.Id}", response);
    }
}
