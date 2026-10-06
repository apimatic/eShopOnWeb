using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions belonging to the authenticated shopper.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, string, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                string? customerReference = httpContext.User.FindFirstValue(ClaimTypes.Name);
                return await HandleAsync(customerReference ?? string.Empty, subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(string customerReference, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse(System.Guid.NewGuid());

        var subscriptions = await subscriptionService.ListSubscriptionsAsync(customerReference);
        foreach (var subscription in subscriptions)
        {
            response.Subscriptions.Add(Map(subscription));
        }

        return Results.Ok(response);
    }

    private static SubscriptionDto Map(Subscription source)
    {
        return new SubscriptionDto
        {
            Id = source.Id,
            State = source.State,
            PlanHandle = source.PlanHandle,
            PlanName = source.PlanName,
            Price = source.Price,
            CurrentPeriodEndsAt = source.CurrentPeriodEndsAt,
            NextAssessmentAt = source.NextAssessmentAt,
            CreatedAt = source.CreatedAt,
            ActivatedAt = source.ActivatedAt,
            CanceledAt = source.CanceledAt,
            PaymentCollectionMethod = source.PaymentCollectionMethod
        };
    }
}
