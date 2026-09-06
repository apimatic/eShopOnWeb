using System.Security.Claims;
using System.Threading;
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
/// Subscribes the caller to a plan.
/// </summary>
/// <remarks>
/// Idempotent by design: a repeated request - a double-click, a retry after a dropped response - returns
/// the subscription that already exists instead of creating a second one, and never creates a second
/// billing customer.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService,
                CancellationToken cancellationToken) =>
            {
                // The subscriber always comes from the token, so the body cannot enroll somebody else.
                request.Subscriber = SubscriptionMapper.ToSubscriber(user, request.FirstName, request.LastName);
                return await HandleAsync(request, billingService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService) =>
        HandleAsync(request, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new BlazorShared.Models.ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "A planHandle is required. Call GET /api/subscription-plans for the available handles."
            });
        }

        var subscriber = request.Subscriber
            ?? throw new SubscriptionBillingException(
                SubscriptionBillingFailure.InvalidRequest,
                "The access token does not identify a user.");

        var enrollment = await billingService.SubscribeAsync(subscriber, request.PlanHandle!, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = enrollment.Subscription.ToDto(),
            AlreadySubscribed = enrollment.AlreadySubscribed
        };

        // A repeat request is not a creation, so it answers 200 rather than a second 201.
        return enrollment.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions", response);
    }
}
