using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioSubscriptionService _maxioService;

    public ListSubscriptionPlansEndpoint(IMaxioSubscriptionService maxioService)
    {
        _maxioService = maxioService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", Handle)
            .RequireAuthorization()
            .Produces<ListSubscriptionPlansResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status500InternalServerError)
            .WithName("ListSubscriptionPlans")
            .WithTags("SubscriptionEndpoints")
            .WithOpenApi();
    }

    private async Task<IResult> Handle(HttpContext context)
    {
        try
        {
            var ct = context.RequestAborted;
            var plans = await _maxioService.ListSubscriptionPlansAsync(ct);

            var response = new ListSubscriptionPlansResponse
            {
                Plans = plans.Select(p => new SubscriptionPlanResponse
                {
                    Id = p.Id,
                    Handle = p.Handle,
                    Name = p.Name,
                    Description = p.Description,
                    PriceInCents = p.PriceInCents,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit
                }).ToList()
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanResponse> Plans { get; set; } = new();
}

public class SubscriptionPlanResponse
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "Month";
}
