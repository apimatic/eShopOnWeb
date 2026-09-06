using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;
using DomainSubscribeRequest = Microsoft.eShopWeb.ApplicationCore.Subscriptions.SubscribeRequest;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling shopper to a plan.
/// </summary>
/// <remarks>
/// Idempotent by design: a repeated or concurrent call for the same account and plan returns the
/// existing subscription with <c>alreadySubscribed: true</c> rather than enrolling twice.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, Subscriber, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateSubscriptionRequest request,
                ISubscriptionService subscriptionService,
                UserManager<ApplicationUser> userManager,
                HttpContext httpContext) =>
            {
                var subscriber = await SubscriberResolver.ResolveAsync(httpContext.User, userManager);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request, subscriber, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                "Subscribes the caller to a plan",
                "Ensures a billing customer exists for the authenticated account and enrolls it on the " +
                "requested plan. Safe to repeat: an account already on the plan gets its existing " +
                "subscription back."));
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        Subscriber subscriber,
        ISubscriptionService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var result = await subscriptionService.SubscribeAsync(
            subscriber,
            new DomainSubscribeRequest
            {
                PlanHandle = request.PlanHandle,
                IdempotencyKey = request.IdempotencyKey,
                FirstName = request.FirstName,
                LastName = request.LastName
            },
            // Deliberately not the request's cancellation token: abandoning an enrollment midway
            // would leave the caller unable to tell whether the subscription was created.
            CancellationToken.None);

        response.Subscription = result.Subscription.ToDto();
        response.AlreadySubscribed = result.AlreadySubscribed;

        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
