using System;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated caller in a subscription plan
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public SubscribeEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                request.UserReference = user.Identity?.Name;
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService)
    {
        return HandleAsync(request, subscriptionService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserReference))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { error = "planHandle is required." });
        }

        return await SubscriptionErrorResults.ExecuteAsync(async () =>
        {
            var subscription = await subscriptionService.SubscribeAsync(request.UserReference, request.PlanHandle, cancellationToken);

            var response = new SubscribeResponse(request.CorrelationId())
            {
                Subscription = _mapper.Map<SubscriptionDto>(subscription)
            };

            return Results.Created($"api/subscriptions/{subscription.Id}", response);
        });
    }
}
