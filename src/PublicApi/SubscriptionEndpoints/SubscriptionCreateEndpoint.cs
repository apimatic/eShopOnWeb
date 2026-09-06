using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the authenticated shopper to a plan
/// </summary>
/// <remarks>
/// Idempotent by design: the shopper's billing customer record is created only if it is missing,
/// and a shopper who is already subscribed to the plan gets that subscription back with 200
/// instead of a second subscription. A double-clicked Subscribe button therefore bills once.
/// </remarks>
public class SubscriptionCreateEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public SubscriptionCreateEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request,
             ClaimsPrincipal principal,
             UserManager<ApplicationUser> userManager,
             ISubscriptionService subscriptionService,
             CancellationToken cancellationToken) =>
            {
                request.Subscriber = await SubscriberResolver.ResolveAsync(principal, userManager);
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        if (request.Subscriber is null)
        {
            // Authenticated with a token whose user no longer exists.
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Problem(
                detail: "'planHandle' is required. Call GET /api/subscription-plans for the available handles.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid subscription request");
        }

        var result = await subscriptionService.SubscribeAsync(
            new SubscribeRequest
            {
                Subscriber = request.Subscriber,
                PlanHandle = request.PlanHandle,
                IdempotencyKey = request.IdempotencyKey
            },
            cancellationToken);

        if (!result.IsSuccess)
        {
            return SubscriptionResults.Problem(result);
        }

        var enrollment = result.Value!;
        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = _mapper.Map<SubscriptionDto>(enrollment.Subscription),
            AlreadySubscribed = enrollment.AlreadyExisted
        };

        return enrollment.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created("/api/my-subscriptions", response);
    }
}
