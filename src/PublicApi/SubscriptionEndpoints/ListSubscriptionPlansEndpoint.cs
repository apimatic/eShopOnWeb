using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (MaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("ListSubscriptionPlans");
    }

    public async Task<IResult> HandleAsync(MaxioSubscriptionService subscriptionService)
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var plans = await subscriptionService.GetSubscriptionPlansAsync();
            response.Plans.AddRange(plans.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                PriceInCents = p.PriceInCents,
                IntervalUnit = p.IntervalUnit,
                Interval = p.Interval
            }));

            return Results.Ok(response);
        }
        catch (MaxioServiceException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; } = new();
}

public class SubscriptionPlanDto
{
    public long Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public int Interval { get; set; }
}
