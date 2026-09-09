using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated user in a subscription plan (creates a Maxio
/// subscription). Idempotent: if the user already holds a live subscription for
/// the plan, the existing subscription is returned.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionService subscriptionService) =>
            {
                var user = _httpContextAccessor.HttpContext?.User
                    ?? throw new UnauthorizedAccessException("No authenticated user identity was found on the request.");

                return await HandleCoreAsync(request, user, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        return await HandleCoreAsync(request, _httpContextAccessor.HttpContext?.User!, subscriptionService);
    }

    private async Task<IResult> HandleCoreAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService)
    {
        var result = await subscriptionService.SubscribeAsync(user, request.ProductHandle ?? string.Empty);
        var subscription = result.Subscription;

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = new SubscriptionDto
            {
                Id = subscription.Id,
                State = subscription.State ?? string.Empty,
                PlanHandle = subscription.Product?.Handle ?? string.Empty,
                PlanName = subscription.Product?.Name ?? string.Empty,
                PriceInCents = subscription.ProductPriceInCents,
                Price = ListSubscriptionPlansEndpoint.FormatPrice(subscription.ProductPriceInCents),
                IntervalUnit = subscription.Product?.IntervalUnit ?? "month",
                NextBillingDate = subscription.CurrentPeriodEndsAt,
                MaxioCustomerId = subscription.Customer?.Id ?? 0,
                AlreadySubscribed = result.AlreadySubscribed
            }
        };

        return Results.Ok(response);
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan (Maxio product) to subscribe to, e.g. "eshop-pro".
    /// </summary>
    public string? ProductHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}
