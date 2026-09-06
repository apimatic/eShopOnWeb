using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, EmptyRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new EmptyRequest(), subscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, ISubscriptionService subscriptionService)
    {
        var response = new ListSubscriptionPlansResponse();
        var plans = await subscriptionService.GetAvailablePlansAsync();

        foreach (var plan in plans)
        {
            response.Plans.Add(new PlanDto
            {
                Handle = plan.Handle,
                Name = plan.Name,
                Description = plan.Description,
                PriceInCents = plan.PriceInCents,
                PriceFormatted = plan.PriceFormatted
            });
        }

        return Results.Ok(response);
    }
}

public class EmptyRequest
{
    public string CorrelationId() => Guid.NewGuid().ToString();
}

public class ListSubscriptionPlansResponse
{
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
    public List<PlanDto> Plans { get; set; } = new();
}

public class PlanDto
{
    public string Handle { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public string PriceFormatted { get; set; } = null!;
}
