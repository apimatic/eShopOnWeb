using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: repeating the
/// same request never creates a second subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, GetUserName(user), subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .ProducesProblem(401)
            .ProducesProblem(404)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, string? userName, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrEmpty(userName))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request?.ProductHandle))
        {
            return Results.BadRequest(new { errors = new[] { "ProductHandle is required." } });
        }

        // Fail fast with a clear 404 for unknown plans instead of a Maxio error.
        var plans = await subscriptionService.ListPlansAsync();
        if (!plans.Any(p => string.Equals(p.Handle, request.ProductHandle, StringComparison.OrdinalIgnoreCase)))
        {
            return Results.NotFound(new { errors = new[] { $"Subscription plan '{request.ProductHandle}' was not found." } });
        }

        CreateSubscriptionResponse response;
        try
        {
            var result = await subscriptionService.SubscribeAsync(userName, userName, request.ProductHandle);
            response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Created = result.Created,
                Subscription = SubscriptionMappers.MapSubscription(result.Subscription)
            };
        }
        catch (BillingException ex)
        {
            return Results.Problem(
                title: "The billing system rejected the subscription request.",
                detail: ex.Message,
                statusCode: ex.StatusCode is >= 400 and < 500 ? 400 : StatusCodes.Status502BadGateway);
        }

        return ToResult(response);
    }

    private static IResult ToResult(CreateSubscriptionResponse response) =>
        response.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);

    private static string? GetUserName(ClaimsPrincipal user) => user.Identity?.Name;
}
