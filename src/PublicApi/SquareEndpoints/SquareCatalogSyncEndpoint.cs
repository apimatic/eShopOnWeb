using System;
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

public class SquareCatalogSyncRequest : BaseRequest
{
    public SquareCatalogSyncRequest(CancellationToken cancellationToken)
    {
        CancellationToken = cancellationToken;
    }

    public CancellationToken CancellationToken { get; }
}

public class SquareCatalogSyncIssueDto
{
    public int CatalogItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class SquareCatalogSyncResponse : BaseResponse
{
    public SquareCatalogSyncResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SquareCatalogSyncResponse()
    {
    }

    public string MerchantId { get; set; } = string.Empty;
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }

    /// <summary>False when some items were skipped or failed — see <see cref="Skipped"/> and <see cref="Failed"/>.</summary>
    public bool Complete { get; set; }
    public List<SquareCatalogSyncIssueDto> Skipped { get; set; } = new();
    public List<SquareCatalogSyncIssueDto> Failed { get; set; } = new();
}

/// <summary>
/// Brings Square's catalog in line with eShop's (operator action).
/// </summary>
public class SquareCatalogSyncEndpoint : IEndpoint<IResult, SquareCatalogSyncRequest, SquareCatalogSyncService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/square/catalog/sync",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, SquareCatalogSyncService syncService) =>
            {
                using var deadline = SquareRequestDeadline.Start(httpContext, SquareConstants.CatalogSyncBudget);
                return await HandleAsync(new SquareCatalogSyncRequest(deadline.Token), syncService);
            })
            .Produces<SquareCatalogSyncResponse>()
            .WithTags("SquareEndpoints");
    }

    public async Task<IResult> HandleAsync(SquareCatalogSyncRequest request, SquareCatalogSyncService syncService)
    {
        var result = await syncService.SyncAsync(request.CancellationToken);
        return Results.Ok(new SquareCatalogSyncResponse(request.CorrelationId())
        {
            MerchantId = result.MerchantId,
            Created = result.Created,
            Updated = result.Updated,
            Unchanged = result.Unchanged,
            Complete = result.Complete,
            Skipped = result.Skipped.Select(ToDto).ToList(),
            Failed = result.Failed.Select(ToDto).ToList(),
        });
    }

    private static SquareCatalogSyncIssueDto ToDto(CatalogSyncIssue issue) => new()
    {
        CatalogItemId = issue.CatalogItemId,
        Name = issue.Name,
        Reason = issue.Reason,
    };
}
