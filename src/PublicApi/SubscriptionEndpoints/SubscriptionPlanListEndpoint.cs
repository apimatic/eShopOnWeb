using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionPlanListRequest, IMaxioClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioClient client) => await HandleAsync(new SubscriptionPlanListRequest(), client))
            .Produces<ListSubscriptionPlanResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionPlanListRequest request, IMaxioClient client)
    {
        var response = new ListSubscriptionPlanResponse();
        // Use stable handles from spec.
        var handles = new[] { "eshop-pro", "basic-plan" };
        foreach (var h in handles)
        {
            var prod = await client.GetProductByHandleAsync(h);
            if (prod != null && prod.PriceInCents > 0)
            {
                response.Plans.Add(new SubscriptionPlanDto
                {
                    Handle = prod.Handle,
                    Name = prod.Name,
                    Price = prod.PriceInCents / 100m,
                    Currency = "USD",
                    Interval = prod.IntervalUnit ?? "month"
                });
            }
        }
        return Results.Ok(response);
    }
}

public class SubscriptionPlanListRequest : BaseRequest
{
}

public class ListSubscriptionPlanResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
    public ListSubscriptionPlanResponse() { }
    public ListSubscriptionPlanResponse(Guid correlationId) : base(correlationId) { }
}
