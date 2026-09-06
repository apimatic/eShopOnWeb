using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List available subscription plans
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioBillingService _billingService;

    public SubscriptionPlanListEndpoint(IMaxioBillingService billingService)
    {
        _billingService = billingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async () => await HandleAsync())
            .RequireAuthorization()
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetSubscriptionPlans");
    }

    public async Task<IResult> HandleAsync()
    {
        try
        {
            var plans = await _billingService.ListSubscriptionPlansAsync();
            var response = new SubscriptionPlanListResponse
            {
                Plans = plans.Select(p => new SubscriptionPlanResponse
                {
                    Id = p.Id,
                    Handle = p.Handle,
                    Name = p.Name,
                    PriceInCents = p.PriceInCents,
                    PriceFormatted = $"${(p.PriceInCents / 100m):F2}/mo",
                    TrialDays = p.TrialDays
                }).ToList()
            };
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

public class SubscriptionPlanResponse
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal PriceInCents { get; set; }
    public string PriceFormatted { get; set; } = string.Empty;
    public int? TrialDays { get; set; }
}

public class SubscriptionPlanListResponse
{
    public List<SubscriptionPlanResponse> Plans { get; set; } = new();
}
