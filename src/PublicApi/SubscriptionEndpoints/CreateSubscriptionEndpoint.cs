using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a subscription plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                request.CustomerReference = httpContext.User.FindFirstValue(ClaimTypes.Name);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerReference))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { message = "A planHandle is required." });
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var subscription = await subscriptionService.SubscribeAsync(request.CustomerReference, request.PlanHandle);
        response.Subscription = Map(subscription);

        return Results.Created($"api/subscriptions/{response.Subscription.Id}", response);
    }

    private static SubscriptionDto Map(ApplicationCore.Entities.SubscriptionAggregate.Subscription source)
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
