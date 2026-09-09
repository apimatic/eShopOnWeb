using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans (Maxio products) available for enrollment.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        var plans = await subscriptionService.ListPlansAsync();

        var response = new ListSubscriptionPlansResponse();
        foreach (var plan in plans)
        {
            response.Plans.Add(new SubscriptionPlanDto
            {
                Handle = plan.Handle ?? string.Empty,
                Name = plan.Name ?? string.Empty,
                Description = plan.Description,
                PriceInCents = plan.PriceInCents,
                Price = FormatPrice(plan.PriceInCents),
                Interval = plan.Interval,
                IntervalUnit = plan.IntervalUnit ?? "month",
                ProductFamilyHandle = plan.ProductFamily?.Handle ?? string.Empty
            });
        }

        return Results.Ok(response);
    }

    internal static string FormatPrice(long priceInCents)
    {
        return $"{priceInCents / 100m:0.00} USD";
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
