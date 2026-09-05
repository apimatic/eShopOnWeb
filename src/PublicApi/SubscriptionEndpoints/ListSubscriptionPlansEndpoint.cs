using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptionService)
    {
        var response = new ListSubscriptionPlansResponse(Guid.NewGuid());

        var plans = await subscriptionService.GetPlansAsync();
        response.Plans.AddRange(plans.Select(p => new SubscriptionPlanResponse
        {
            Id = p.Id,
            Name = p.Name,
            Handle = p.Handle,
            Price = p.Price,
            BillingPeriod = $"{p.Interval} {p.IntervalUnit}(s)"
        }));

        return Results.Ok(response);
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }

    public List<SubscriptionPlanResponse> Plans { get; set; } = new();
}

public class SubscriptionPlanResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Handle { get; set; } = "";
    public decimal Price { get; set; }
    public string BillingPeriod { get; set; } = "";
}
