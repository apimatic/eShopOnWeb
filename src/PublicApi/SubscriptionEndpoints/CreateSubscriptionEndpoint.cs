using System.Collections.Generic;
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
/// Subscribes the signed-in shopper to a plan. Ensures a Maxio customer exists
/// for the shopper and never creates a duplicate subscription for the same plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        IMaxioSubscriptionService subscriptionService,
        IHttpContextAccessor httpContextAccessor)
    {
        _subscriptionService = subscriptionService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request) =>
            {
                return await HandleAsync(request);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        var username = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.ProductHandle), new[] { "ProductHandle is required." } }
            });
        }

        try
        {
            var summary = await _subscriptionService.SubscribeAsync(username, request.ProductHandle!);

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = new SubscriptionDto
                {
                    MaxioSubscriptionId = summary.MaxioSubscriptionId,
                    State = summary.State,
                    PlanHandle = summary.PlanHandle,
                    PlanName = summary.PlanName,
                    Price = summary.PriceInCents / 100m,
                    PriceInCents = summary.PriceInCents,
                    Currency = summary.Currency,
                    NextBillingDate = summary.NextBillingDate,
                    CreatedAt = summary.CreatedAt
                }
            };

            return Results.Ok(response);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 404)
        {
            return Results.NotFound(new { errors = ex.Errors });
        }
    }
}