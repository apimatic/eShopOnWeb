using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the authenticated shopper to a plan
/// </summary>
/// <remarks>
/// Repeating the call for the same shopper and plan is safe: the existing subscription is
/// returned with 200 instead of a second one being created with 201.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, HttpContext httpContext,
                UserManager<ApplicationUser> userManager, ISubscriptionService subscriptionService) =>
            {
                var userName = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userName))
                {
                    return Results.Unauthorized();
                }

                request.UserName = userName;

                // eShopOnWeb seeds users whose name is their email, but do not assume it: the
                // billing customer is created with the address on the account.
                var account = await userManager.FindByNameAsync(userName);
                request.Email = string.IsNullOrWhiteSpace(account?.Email) ? userName : account!.Email!;

                if (httpContext.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var headerKey) &&
                    !string.IsNullOrWhiteSpace(headerKey))
                {
                    request.IdempotencyKey = headerKey.ToString();
                }

                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { message = "planHandle is required." });
        }

        var subscribeRequest = new SubscribeRequest(
            customer: new BillingCustomerProfile(
                userIdentifier: request.UserName,
                email: string.IsNullOrWhiteSpace(request.Email) ? request.UserName : request.Email),
            planHandle: request.PlanHandle.Trim(),
            pricePointHandle: request.PricePointHandle,
            idempotencyKey: request.IdempotencyKey);

        var result = await subscriptionService.SubscribeAsync(subscribeRequest);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = result.Subscription.ToDto(),
            Created = result.Created,
            CustomerCreated = result.CustomerCreated
        };

        // A repeat signup is not an error, but it is not a creation either: answer 200 so the
        // caller can tell the two apart without inspecting the body.
        return result.Created
            ? Results.Created("/api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
