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
/// Subscribes the authenticated user to a plan. Idempotent: ensures a single Maxio customer
/// exists for the user and returns the existing subscription if one is already live for the plan,
/// so a double-click never creates duplicate customers or subscriptions.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user,
                ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                request.Caller = SubscriptionMappings.ToBillingCustomer(user);
                return await HandleAsync(request, billingService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints")
            .WithName("subscriptions.create");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken = default)
    {
        if (request.Caller is null)
        {
            throw new SubscriptionValidationException("The caller identity could not be resolved from the token.");
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            throw new SubscriptionValidationException("A 'planHandle' is required. Choose one from GET /api/subscription-plans.");
        }

        var subscription = await billingService.SubscribeAsync(request.Caller, request.PlanHandle.Trim(), cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = subscription.ToDto(),
            AlreadySubscribed = !subscription.WasCreated
        };

        return subscription.WasCreated
            ? Results.Created($"api/subscriptions/{subscription.Id}", response)
            : Results.Ok(response);
    }

    // Satisfies IEndpoint<IResult, TRequest, TParam>; the route delegate injects the caller identity.
    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
        => HandleAsync(request, billingService, default);
}
