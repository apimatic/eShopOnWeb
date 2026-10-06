using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SquareEndpoints;

/// <summary>Brings the Square catalog in line with eShop's catalog (names and prices).</summary>
public class SquareCatalogSyncEndpoint : IEndpoint<IResult, SquareCatalogSync, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/square/catalog/sync",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SquareCatalogSync sync, HttpContext context) =>
            {
                return await HandleAsync(sync, context.RequestAborted);
            })
            .Produces<SquareCatalogSyncResponse>()
            .WithTags("SquareEndpoints");
    }

    public async Task<IResult> HandleAsync(SquareCatalogSync sync, CancellationToken requestAborted)
    {
        using var deadline = SquareTimeouts.Deadline(requestAborted, SquareTimeouts.CatalogSync);
        var result = await sync.SyncAllAsync(deadline.Token);
        return Results.Ok(new SquareCatalogSyncResponse
        {
            MerchantId = result.MerchantId,
            Created = result.Created,
            Updated = result.Updated,
            Unchanged = result.Unchanged,
            Failed = result.Failed,
            Items = result.Items.Select(i => new SquareCatalogSyncItemDto
            {
                CatalogItemId = i.CatalogItemId,
                Name = i.Name,
                Outcome = i.Outcome,
                SquareItemId = i.SquareItemId,
                Error = i.Error,
            }).ToList(),
        });
    }
}

public class SquareCatalogSyncResponse : BaseResponse
{
    public string MerchantId { get; set; } = string.Empty;
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    /// <summary>Items that could not be synced this time (see <see cref="Items"/>); running the sync again retries them.</summary>
    public int Failed { get; set; }
    public List<SquareCatalogSyncItemDto> Items { get; set; } = new();
}

public class SquareCatalogSyncItemDto
{
    public int CatalogItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary><c>created</c>, <c>updated</c>, <c>unchanged</c> or <c>failed</c>.</summary>
    public string Outcome { get; set; } = string.Empty;
    public string? SquareItemId { get; set; }
    public string? Error { get; set; }
}
