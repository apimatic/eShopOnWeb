using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated eShopOnWeb user to a plan. A Maxio customer is
/// ensured for the user (idempotently) and they are enrolled in the requested
/// product handle. Subscribing twice to the same plan returns the existing
/// live subscription instead of creating a duplicate.
/// </summary>
public class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscriptionCreateRequest, ISubscriptionService>
{
    public SubscriptionCreateEndpoint(ISubscriptionService subscriptionService, IHttpContextAccessor httpContextAccessor)
    {
        SubscriptionService = subscriptionService;
        HttpContextAccessor = httpContextAccessor;
    }

    private ISubscriptionService SubscriptionService { get; }

    private IHttpContextAccessor HttpContextAccessor { get; }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (SubscriptionCreateRequest request, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscriptionCreateResponse>()
            .RequireAuthorization(MaxioEndpointResults.JwtAuthorize())
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionCreateRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.ProductHandle), new[] { "A product handle is required." } }
            });
        }

        var httpContext = HttpContextAccessor.HttpContext;
        var user = httpContext?.User;
        var userName = user?.Identity?.Name;
        if (string.IsNullOrEmpty(userName) || !(user?.Identity?.IsAuthenticated ?? false))
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscription = await SubscriptionService.SubscribeAsync(userName, request.ProductHandle, CancellationToken.None);
            return Results.Created($"/api/my-subscriptions", new SubscriptionCreateResponse(request.CorrelationId())
            {
                Subscription = subscription
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
        catch (MaxioApiException ex)
        {
            return MaxioEndpointResults.FromMaxioException(ex);
        }
    }
}
