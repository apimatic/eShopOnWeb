using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List subscription plans
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint<IResult, ListPlansRequest, IMaxioApiClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioApiClient maxioClient) =>
            {
                return await HandleAsync(new ListPlansRequest(), maxioClient);
            })
            .Produces<SubscriptionPlansListResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPlansRequest request, IMaxioApiClient maxioClient)
    {
        try
        {
            var products = await maxioClient.ListProductsAsync();
            var response = new SubscriptionPlansListResponse(Guid.NewGuid());
            response.Plans.AddRange(products.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                Description = p.Description,
                Price = p.PriceInCents / 100m,
                Interval = p.Interval.ToString(),
                IntervalUnit = p.IntervalUnit
            }));
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class ListPlansRequest : BaseRequest
{
}

public class SubscriptionPlansListResponse : BaseResponse
{
    public SubscriptionPlansListResponse(Guid correlationId) : base(correlationId) { }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
