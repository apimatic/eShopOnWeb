using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", Handle)
            .Produces<SubscriptionPlansListResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("ListSubscriptionPlans");
    }

    private static async Task<IResult> Handle(IMaxioService maxioService)
    {
        var response = new SubscriptionPlansListResponse(Guid.NewGuid());

        try
        {
            var plans = await maxioService.GetPlansAsync();
            response.Plans.AddRange(plans.Select(p => new SubscriptionPlanDto
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
        catch (Exception ex)
        {
            response.ErrorMessage = $"Failed to retrieve subscription plans: {ex.Message}";
            return Results.BadRequest(response);
        }
    }
}

public class EmptyRequest : BaseRequest
{
}

public class SubscriptionPlansListResponse : BaseResponse
{
    public SubscriptionPlansListResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionPlansListResponse()
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new List<SubscriptionPlanDto>();
    public string? ErrorMessage { get; set; }
}
