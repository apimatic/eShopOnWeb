using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: repeating the call for a plan
/// the user is already subscribed to returns the existing subscription instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, SubscriptionBillingService>
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
            (CreateSubscriptionRequest request, SubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request, billingService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, SubscriptionBillingService billingService)
    {
        var username = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest(new CreateSubscriptionResponse(request.CorrelationId()));
        }

        var (subscription, alreadyExisted) = await billingService.SubscribeAsync(username, request.ProductHandle);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = subscription,
            AlreadySubscribed = alreadyExisted
        };

        return alreadyExisted
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public CreateSubscriptionResponse() { }

    public SubscriptionDto? Subscription { get; set; }

    /// <summary>True when the user already had a live subscription to this plan and no new one was created.</summary>
    public bool AlreadySubscribed { get; set; }
}
