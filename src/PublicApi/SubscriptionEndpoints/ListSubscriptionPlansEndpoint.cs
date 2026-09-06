using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioApiService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioApiService maxioApi) =>
            {
                return await HandleAsync(maxioApi);
            })
            .RequireAuthorization()
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioApiService maxioApi)
    {
        var response = new ListSubscriptionPlansResponse(Guid.NewGuid());
        response.Plans = new List<SubscriptionPlanDto>();

        var proPlan = await maxioApi.GetProductByHandleAsync("eshop-pro");
        if (proPlan != null)
        {
            response.Plans.Add(new SubscriptionPlanDto
            {
                Id = proPlan.Id,
                Handle = proPlan.Handle,
                Name = proPlan.Name,
                Description = proPlan.Description,
                PricePerMonth = proPlan.PriceInCents / 100m,
                BillingInterval = $"Every {proPlan.Interval} {proPlan.IntervalUnit}",
                HasTrial = proPlan.TrialInterval.HasValue && proPlan.TrialInterval > 0,
                TrialDays = proPlan.TrialInterval
            });
        }

        var basicPlan = await maxioApi.GetProductByHandleAsync("basic-plan");
        if (basicPlan != null)
        {
            response.Plans.Add(new SubscriptionPlanDto
            {
                Id = basicPlan.Id,
                Handle = basicPlan.Handle,
                Name = basicPlan.Name,
                Description = basicPlan.Description,
                PricePerMonth = basicPlan.PriceInCents / 100m,
                BillingInterval = $"Every {basicPlan.Interval} {basicPlan.IntervalUnit}",
                HasTrial = basicPlan.TrialInterval.HasValue && basicPlan.TrialInterval > 0,
                TrialDays = basicPlan.TrialInterval
            });
        }

        return Results.Ok(response);
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
