using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user (identity from the JWT) to a plan. Idempotent:
/// a repeated POST for the same user+plan returns the existing subscription instead
/// of creating a second one.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest? request, IMaxioSubscriptionService subscriptionService) =>
                await HandleAsync(request ?? new CreateSubscriptionRequest(), subscriptionService))
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var username = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        SubscribeResult result;
        try
        {
            result = await subscriptionService.SubscribeAsync(username, username, request.PlanHandle);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { correlationId = request.CorrelationId(), errors = ex.ApiErrors });
        }
        catch (MaxioApiException ex) when (ex.IsValidationConflict)
        {
            return Results.Conflict(new { correlationId = request.CorrelationId(), errors = ex.ApiErrors });
        }

        response.AlreadySubscribed = result.AlreadySubscribed;
        response.CreatedCustomer = result.CreatedCustomer;
        response.Subscription = Map(result.Subscription);

        return Results.Ok(response);
    }

    internal static SubscriptionDto Map(SubscriptionSummary summary) => new()
    {
        SubscriptionId = summary.MaxioSubscriptionId,
        State = summary.State,
        PlanHandle = summary.PlanHandle,
        PlanName = summary.PlanName,
        PriceInCents = summary.PriceInCents,
        Interval = summary.Interval,
        IntervalUnit = summary.IntervalUnit,
        NextBillingDateUtc = summary.NextBillingDate?.UtcDateTime,
        CustomerReference = summary.CustomerReference
    };
}
