using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansEndpoint : IEndpoint<IResult, EmptyRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new EmptyRequest(), subscriptionService);
            })
            .Produces<GetSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetSubscriptionPlans")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(EmptyRequest request)
    {
        throw new NotImplementedException("This method is not used; use the other overload.");
    }

    private async Task<IResult> HandleAsync(EmptyRequest request, IMaxioSubscriptionService subscriptionService)
    {
        var plans = await subscriptionService.GetSubscriptionPlansAsync();
        var response = new GetSubscriptionPlansResponse(request.CorrelationId());
        response.Plans.AddRange(plans.Select(p => new SubscriptionPlanResponse
        {
            Id = p.Id,
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            Price = p.Price,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit
        }));
        return Results.Ok(response);
    }
}

public class EmptyRequest : BaseRequest
{
}

public class SubscriptionPlanResponse
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class GetSubscriptionPlansResponse : BaseResponse
{
    public GetSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionPlanResponse> Plans { get; } = new();
}
