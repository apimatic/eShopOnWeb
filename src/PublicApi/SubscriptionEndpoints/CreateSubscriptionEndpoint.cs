using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan.
/// </summary>
/// <remarks>
/// The billing-system customer record is created on first use, so a shopper never has to be provisioned
/// up front. The operation is idempotent per (shopper, plan): repeating it while a live subscription
/// exists returns that subscription with <c>alreadySubscribed</c> set instead of enrolling twice.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService, CancellationToken>
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
            (SubscribeRequest request,
             ClaimsPrincipal user,
             ISubscriberAccessor subscribers,
             ISubscriptionBillingService billing,
             CancellationToken cancellationToken) =>
            {
                var subscriber = await subscribers.GetSubscriberAsync(user, cancellationToken);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                request.Subscriber = subscriber;

                return await HandleAsync(request, billing, cancellationToken);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billing, CancellationToken cancellationToken)
    {
        if (request.Subscriber is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Json(
                new ErrorDetails
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Message = "planHandle is required. Call GET /api/subscription-plans for the available handles."
                },
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await billing.SubscribeAsync(request.Subscriber, request.PlanHandle.Trim(), cancellationToken);

        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = _mapper.Map<SubscriptionDto>(result.Subscription),
            AlreadySubscribed = result.AlreadyExisted
        };

        // A repeat of an enrollment that already happened is not a creation, so it does not answer 201.
        return result.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created("/api/my-subscriptions", response);
    }
}
