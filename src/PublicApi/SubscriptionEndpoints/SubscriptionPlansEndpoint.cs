using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class SubscriptionPlansEndpoint : IEndpoint<IResult, ListPlansRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioService maxioService) =>
            {
                return await HandleAsync(new ListPlansRequest(), maxioService);
            })
            .Produces<ListPlansResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPlansRequest request)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(ListPlansRequest request, IMaxioService maxioService)
    {
        var response = new ListPlansResponse(request.CorrelationId());

        try
        {
            var plans = await maxioService.GetAvailablePlansAsync();
            foreach (var plan in plans)
            {
                response.Plans.Add(new PlanDto
                {
                    ProductId = plan.ProductId,
                    Handle = plan.Handle,
                    Name = plan.Name,
                    PriceInCents = plan.PriceInCents,
                    Interval = plan.Interval,
                    IntervalUnit = plan.IntervalUnit
                });
            }

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
