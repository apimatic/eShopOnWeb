using System;
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
/// Enrolls the authenticated shopper on a subscription plan. Idempotent: repeating
/// the call for the same user and plan returns the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal principal) =>
            {
                return await HandleAsync(request, subscriptionService, principal);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
        => HandleAsync(request, subscriptionService, null);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal? principal)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var (userId, email) = ResolveCallerIdentity(principal);

        var result = await subscriptionService.SubscribeAsync(userId, email, request.PlanHandle ?? string.Empty);
        if (result.IsSuccess)
        {
            response.Subscription = ToDto(result.Value.Subscription);
            return result.Value.CreatedNew
                ? Results.Created("api/my-subscriptions", response)
                : Results.Ok(response);
        }

        if (result.Status == Ardalis.Result.ResultStatus.Invalid)
        {
            response.Errors.AddRange(result.ValidationErrors.Select(e => e.ErrorMessage).Where(m => !string.IsNullOrEmpty(m)));
            return Results.BadRequest(response);
        }

        response.Errors.Add("The billing system is not answering correctly; the subscription could not be confirmed right now.");
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }

    /// <summary>
    /// The JWT carries the username as the <c>Name</c> claim and, on tokens issued by
    /// this API, the account's email address. Test-forged tokens may only carry the
    /// name; the service falls back to it (usernames in eShopOnWeb are emails).
    /// </summary>
    internal static (string UserId, string? Email) ResolveCallerIdentity(ClaimsPrincipal? principal)
    {
        var userName = principal?.Identity?.Name ?? string.Empty;
        var emailClaim = principal?.FindFirstValue(ClaimTypes.Email);
        return (userName, emailClaim);
    }

    internal static SubscriptionSummaryDto ToDto(ApplicationCore.Models.Subscription.SubscriptionSummary summary) => new()
    {
        SubscriptionId = summary.SubscriptionId,
        PlanHandle = summary.PlanHandle,
        PlanResolved = summary.PlanResolved,
        PriceInCents = summary.PriceInCents,
        State = summary.State,
        NextBillingDate = summary.NextBillingDate,
        CreatedAt = summary.CreatedAt
    };
}