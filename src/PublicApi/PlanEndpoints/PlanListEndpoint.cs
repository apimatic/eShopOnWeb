using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PlanEndpoints;

public class PlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public int CatalogBrandId { get; set; }
    public string CatalogBrandName { get; set; } = string.Empty;

    public static PlanDto From(OfferedPlan plan) => new()
    {
        Id = plan.Id,
        Name = plan.Name,
        Handle = plan.Handle,
        Description = plan.Description,
        Price = plan.PriceInCents / 100m,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        CatalogBrandId = plan.CatalogBrandId,
        CatalogBrandName = plan.CatalogBrandName,
    };
}

public class ListPlansResponse : BaseResponse
{
    public ListPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListPlansResponse()
    {
    }

    public List<PlanDto> Plans { get; set; } = new();
}

public class ListPlansRequest : BaseRequest
{
}

/// <summary>
/// List the plans a shopper can subscribe to
/// </summary>
public class PlanListEndpoint : IEndpoint<IResult, ListPlansRequest, PlanCatalog>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            async (PlanCatalog catalog, HttpContext http) =>
            {
                return await HandleAsync(new ListPlansRequest(), catalog, http.RequestAborted);
            })
            .Produces<ListPlansResponse>()
            .WithTags("PlanEndpoints");
    }

    public Task<IResult> HandleAsync(ListPlansRequest request, PlanCatalog catalog)
        => HandleAsync(request, catalog, default);

    public async Task<IResult> HandleAsync(ListPlansRequest request, PlanCatalog catalog, System.Threading.CancellationToken ct)
    {
        var response = new ListPlansResponse(request.CorrelationId());
        var plans = await catalog.ListOfferedAsync(ct);
        response.Plans.AddRange(plans.Select(PlanDto.From));
        return Results.Ok(response);
    }
}
