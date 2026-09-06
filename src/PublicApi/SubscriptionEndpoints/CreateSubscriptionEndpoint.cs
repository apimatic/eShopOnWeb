using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Creates a subscription for the current user
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService, HttpContext context) =>
            {
                var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var userEmail = context.User.FindFirst(ClaimTypes.Email)?.Value;
                request.UserId = userId ?? "";
                request.UserEmail = userEmail ?? "";
                request.FirstName = context.User.FindFirst("given_name")?.Value ?? "Customer";
                request.LastName = context.User.FindFirst("family_name")?.Value ?? "Account";

                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var subscription = await subscriptionService.CreateSubscriptionAsync(
            request.UserId,
            request.UserEmail,
            request.FirstName,
            request.LastName,
            request.PlanHandle
        );

        response.Subscription = new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State,
            CustomerId = subscription.CustomerId,
            ProductName = subscription.ProductName,
            ProductPriceInCents = subscription.ProductPriceInCents,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            UpdatedAt = subscription.UpdatedAt
        };

        return Results.Ok(response);
    }
}
