using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionPlanListRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new SubscriptionPlanListRequest(Guid.NewGuid()), subscriptionService);
            })
            .RequireAuthorization()
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithSummary("List available subscription plans");
    }

    public async Task<IResult> HandleAsync(SubscriptionPlanListRequest request, IMaxioSubscriptionService subscriptionService)
    {
        try
        {
            var plans = await subscriptionService.GetAvailablePlansAsync();

            var response = new SubscriptionPlanListResponse(request.CorrelationId());
            response.Plans.AddRange(plans.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            }));

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.StatusCode(500);
        }
    }
}

public class SubscriptionPlanListRequest : BaseRequest
{
    public SubscriptionPlanListRequest(Guid correlationId) : base()
    {
        base._correlationId = correlationId;
    }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class SubscriptionPlanListResponse : BaseResponse
{
    public SubscriptionPlanListResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionPlanListResponse()
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
