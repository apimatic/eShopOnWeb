using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available to eShopOnWeb shoppers (Maxio products of the
/// configured product family).
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
           .RequireAuthorization()
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var plans = await subscriptionService.GetPlansAsync();
            response.Plans.AddRange(plans.Select(p => new SubscriptionPlanDto
            {
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                Price = p.Price,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit,
                RequiresPaymentMethod = p.RequiresPaymentMethod
            }));
        }
        catch (MaxioApiException ex)
        {
            return Results.Json(new { message = "The billing service returned an error.", detail = ex.Message },
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Ok(response);
    }
}
