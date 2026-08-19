using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.CatalogSupplierEndpoints;

/// <summary>
/// Reports the status and outcome of a single sync. Operator-only.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class GetCatalogSyncByIdEndpoint : EndpointBaseAsync
    .WithRequest<GetCatalogSyncByIdRequest>
    .WithActionResult<CatalogSyncResponse>
{
    private readonly IRepository<CatalogSync> _syncRepository;

    public GetCatalogSyncByIdEndpoint(IRepository<CatalogSync> syncRepository)
    {
        _syncRepository = syncRepository;
    }

    [HttpGet("api/catalog/syncs/{syncId}")]
    [SwaggerOperation(
        Summary = "Gets the status and outcome of a sync",
        Description = "Reports whether a sync is still running, captured the whole listing, or captured only part of it",
        OperationId = "catalog.syncs.getById",
        Tags = new[] { "CatalogSupplierEndpoints" })
    ]
    public override async Task<ActionResult<CatalogSyncResponse>> HandleAsync(
        GetCatalogSyncByIdRequest request,
        CancellationToken cancellationToken = default)
    {
        var sync = await _syncRepository.GetByIdAsync(request.SyncId, cancellationToken);
        if (sync is null)
        {
            return NotFound($"Sync {request.SyncId} was not found.");
        }

        var response = new CatalogSyncResponse(request.CorrelationId())
        {
            SyncId = sync.Id,
            SupplierId = sync.SupplierId,
            Status = sync.Status.ToString(),
            ItemsFound = sync.ItemsFound,
            ItemsImported = sync.ItemsImported,
            StartedDate = sync.StartedDate,
            CompletedDate = sync.CompletedDate,
            ErrorMessage = sync.ErrorMessage
        };

        return Ok(response);
    }
}
