using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlanRequest : BaseMessage
{
}

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ListSubscriptionPlanRequest, MaxioService>
{
    private readonly MaxioService _maxioService;

    public SubscriptionPlanListEndpoint(MaxioService maxioService)
    {
        _maxioService = maxioService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (MaxioService maxioService) =>
            {
                return await HandleAsync(new ListSubscriptionPlanRequest(), maxioService);
            })
            .Produces<ListSubscriptionPlanResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlanRequest request, MaxioService maxioService)
    {
        var response = new ListSubscriptionPlanResponse(request.CorrelationId());

        try
        {
            var plans = await maxioService.GetPlansAsync();
            response.Plans = plans.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                PriceInDollars = p.PriceInDollars,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit,
                RequireCreditCard = p.RequireCreditCard
            }).ToList();
            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(ex.Message, statusCode: 502);
        }
    }
}
