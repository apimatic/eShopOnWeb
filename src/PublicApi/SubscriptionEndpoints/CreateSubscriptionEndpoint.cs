using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the caller to a plan.
/// </summary>
/// <remarks>
/// The subscriber is taken from the bearer token, never from the request body. The operation is
/// idempotent per (caller, plan): repeating it while a live subscription to that plan exists returns
/// the existing subscription with <c>200 OK</c> instead of creating a second one.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionApiService>
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
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionApiService subscriptionService) =>
            {
                return await HandleAsync(request, user, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal user,
        ISubscriptionApiService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest($"{nameof(request.PlanHandle)} is required.");
        }

        // Enrollment is deliberately not tied to the request's cancellation token: abandoning a
        // signup half way through because the caller hung up would leave the shopper unsure whether
        // they were charged. The provider call is bounded by its own HTTP timeout instead.
        var subscriber = await subscriptionService.ResolveSubscriberAsync(user, CancellationToken.None);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var enrollment = await subscriptionService.SubscribeAsync(
            subscriber,
            request.PlanHandle.Trim(),
            CancellationToken.None);

        response.Subscription = _mapper.Map<SubscriptionDto>(enrollment.Subscription);
        response.AlreadySubscribed = enrollment.AlreadyExisted;

        // 201 carries no Location header: there is no per-subscription route to point at, and
        // pointing at the collection would misidentify the created resource.
        return enrollment.AlreadyExisted
            ? Results.Ok(response)
            : Results.Json(response, statusCode: StatusCodes.Status201Created);
    }
}
