using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public ListSubscriptionPlansEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async () =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest());
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetSubscriptionPlans");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request)
    {
        var cancellationToken = CancellationToken.None;
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        try
        {
            var plans = await _subscriptionService.GetAvailablePlansAsync(cancellationToken);
            foreach (var plan in plans)
            {
                response.Plans.Add(new SubscriptionPlanDto
                {
                    Id = plan.Id,
                    Handle = plan.Handle,
                    Name = plan.Name,
                    Description = plan.Description,
                    PricePerMonth = plan.PriceInCents / 100m,
                    Interval = plan.Interval.ToString(),
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
