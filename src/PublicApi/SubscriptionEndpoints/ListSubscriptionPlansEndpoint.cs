using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.MaxioIntegration;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists available subscription plans
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, EmptyRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [SwaggerOperation(
                Summary = "List subscription plans",
                Description = "Returns all available subscription plans",
                OperationId = "subscriptions.listPlans",
                Tags = new[] { "SubscriptionEndpoints" })]
            async (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new EmptyRequest(), subscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, ISubscriptionService subscriptionService)
    {
        var plans = await subscriptionService.GetAvailablePlansAsync();
        var response = new ListSubscriptionPlansResponse();
        response.Plans = plans.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Handle,
            Name = p.Name,
            PriceInCents = p.PriceInCents,
            Description = p.Description
        }).ToList();

        return Results.Ok(response);
    }
}

public class EmptyRequest : BaseRequest
{
}

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal PriceInCents { get; set; }
    public string Description { get; set; } = string.Empty;
}
