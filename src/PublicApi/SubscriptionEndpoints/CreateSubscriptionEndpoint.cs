using System.Collections.Generic;
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
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated caller to a plan.
///
/// The call is idempotent. It ensures a billing customer exists for the caller, then enrolls them,
/// and answers 201 Created for a new subscription or 200 OK with the existing one when the caller
/// is already on that plan - so a double-clicked Subscribe button never bills twice.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
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
            (CreateSubscriptionRequest request, ClaimsPrincipal user,
             SubscriberIdentityResolver identityResolver, ISubscriptionBillingService billingService,
             CancellationToken cancellationToken) =>
            {
                var subscriber = await identityResolver.ResolveAsync(user);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                request.Subscriber = subscriber;
                request.CancellationToken = cancellationToken;
                return await HandleAsync(request, billingService);
            })
            .Accepts<CreateSubscriptionRequest>("application/json")
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        if (request.Subscriber is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["planHandle"] = new[] { "A planHandle is required. Call GET /api/subscription-plans for the available handles." }
            });
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var result = await billingService.SubscribeAsync(
            new SubscribeRequest(request.Subscriber, request.PlanHandle!, request.IdempotencyKey),
            request.CancellationToken);

        response.Subscription = _mapper.Map<SubscriptionDto>(result.Subscription);
        response.AlreadySubscribed = result.AlreadySubscribed;

        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions#{result.Subscription.Id}", response);
    }
}
