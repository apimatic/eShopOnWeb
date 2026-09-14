using System.Threading;
using System.Threading.Tasks;
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
/// Subscribes the signed-in shopper to a plan. Idempotent: subscribing twice to the same
/// plan returns the existing subscription instead of creating a duplicate.
/// Route: POST /api/subscriptions
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        ISubscriptionBillingService subscriptionBillingService,
        IHttpContextAccessor httpContextAccessor)
    {
        _subscriptionBillingService = subscriptionBillingService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (CreateSubscriptionRequest request) => await HandleAsync(request))
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "A plan handle is required. POST a JSON body such as { \"planHandle\": \"eshop-pro\" }."
            });
        }

        var subscriberKey = SubscriberKey.From(_httpContextAccessor.HttpContext?.User);
        if (string.IsNullOrWhiteSpace(subscriberKey))
        {
            return Results.Unauthorized();
        }

        var cancellationToken = _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        var result = await _subscriptionBillingService.SubscribeAsync(subscriberKey, request.PlanHandle.Trim(), cancellationToken);

        var response = new CreateSubscriptionResponse
        {
            Subscription = result.Subscription,
            Created = result.Created
        };

        return Results.Ok(response);
    }
}
