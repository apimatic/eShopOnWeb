using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: the caller's identity (from the
/// JWT) is the stable customer reference, so a double-submit resolves to the same Maxio customer
/// and subscription instead of creating duplicates.
/// </summary>
public class SubscriptionCreateEndpoint
    : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService)
                => await HandleAsync(request, user, billingService))
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService)
    {
        var username = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("A plan handle is required to subscribe.");
        }

        var (firstName, lastName) = DeriveName(username);

        var subscribeRequest = new SubscribeRequest
        {
            CustomerReference = username,
            Email = username,
            FirstName = firstName,
            LastName = lastName,
            PlanHandle = request.PlanHandle.Trim()
        };

        var subscription = await billingService.SubscribeAsync(subscribeRequest);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = subscription.ToDto()
        };

        return Results.Created($"api/subscriptions/{response.Subscription.Id}", response);
    }

    /// <summary>
    /// Derives a first/last name for the billing customer profile from the eShop identity, which is
    /// an email address. Maxio requires non-empty names; we fill them from the real identity rather
    /// than fabricating unrelated data.
    /// </summary>
    private static (string FirstName, string LastName) DeriveName(string username)
    {
        var localPart = username.Contains('@') ? username[..username.IndexOf('@')] : username;

        var dot = localPart.IndexOf('.');
        if (dot > 0 && dot < localPart.Length - 1)
        {
            return (localPart[..dot], localPart[(dot + 1)..]);
        }

        return (localPart, "eShopOnWeb");
    }
}
