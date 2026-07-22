using System;
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
/// UC2 — records pay-as-you-go usage. Without a subscription id it targets the caller's own active
/// subscription; with one it targets any subscription and is restricted to administrators.
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RecordUsageRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.AuthenticatedUserName = SubscriptionEndpointResults.GetUserName(user);
                request.IsAdministrator = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        if (request.AuthenticatedUserName is null)
        {
            return Results.Unauthorized();
        }

        // Invalid quantities never reach the billing provider.
        if (request.Quantity <= 0)
        {
            return Results.BadRequest(new { error = "quantity must be greater than zero." });
        }

        if (request.SubscriptionId.HasValue && !request.IsAdministrator)
        {
            // The scheme is named explicitly: identity also registers a cookie scheme, and letting it
            // handle the forbid would answer an API caller with a login redirect instead of a 403.
            return Results.Forbid(authenticationSchemes: new[] { JwtBearerDefaults.AuthenticationScheme });
        }

        var response = new RecordUsageResponse(request.CorrelationId());

        try
        {
            var summary = request.SubscriptionId.HasValue
                ? await subscriptionService.RecordUsageForSubscriptionAsync(request.SubscriptionId.Value, request.Quantity, request.Memo)
                : await subscriptionService.RecordUsageAsync(request.AuthenticatedUserName, request.Quantity, request.Memo);

            response.Usage = UsageSummaryDto.From(summary);
        }
        catch (Exception ex) when (SubscriptionEndpointResults.IsExpected(ex))
        {
            return SubscriptionEndpointResults.FromException(ex);
        }

        return Results.Ok(response);
    }
}
