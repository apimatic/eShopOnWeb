using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the signed-in shopper to a plan, creating their billing-system customer if this is
/// their first subscription.
/// </summary>
/// <remarks>
/// The call is idempotent. Subscribing to a plan the shopper is already on returns 200 with the
/// existing subscription and <c>alreadySubscribed: true</c>; only a genuinely new enrollment
/// returns 201. Double-clicking Subscribe therefore cannot produce two customers or two subscriptions.
/// </remarks>
public class CreateSubscriptionEndpoint
    : IEndpoint<IResult, CreateSubscriptionRequest, SubscriberIdentity, ISubscriptionBillingService>
{
    private readonly IMapper _mapper;

    public CreateSubscriptionEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request,
             ClaimsPrincipal principal,
             ISubscriberResolver subscriberResolver,
             ISubscriptionBillingService billingService,
             CancellationToken cancellationToken) =>
            {
                var subscriber = await subscriberResolver.ResolveAsync(principal, cancellationToken);

                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request ?? new CreateSubscriptionRequest(), subscriber, billingService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                "Subscribes the signed-in shopper to a plan",
                "Idempotent: returns 201 for a new enrollment and 200 with the existing subscription if the shopper is already on the plan."));
    }

    public Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        SubscriberIdentity subscriber,
        ISubscriptionBillingService billingService) =>
        HandleAsync(request, subscriber, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        SubscriberIdentity subscriber,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var enrollment = await billingService.SubscribeAsync(subscriber, request.PlanHandle, cancellationToken);

        response.Subscription = _mapper.Map<SubscriptionDto>(enrollment.Subscription);
        response.AlreadySubscribed = enrollment.AlreadyEnrolled;

        return enrollment.AlreadyEnrolled
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
