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

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), subscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, ISubscriptionService subscriptionService)
    {
        try
        {
            var plans = await subscriptionService.GetAvailablePlansAsync();

            var response = new ListSubscriptionPlansResponse()
            {
                Plans = plans.Select(p => new SubscriptionPlanDto
                {
                    Id = p.Id,
                    Handle = p.Handle,
                    Name = p.Name,
                    Price = p.PriceInCents / 100m,
                    IntervalDays = p.IntervalDays
                }).ToList()
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: 500);
        }
    }
}

public class ListSubscriptionPlansRequest : BaseRequest
{
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
