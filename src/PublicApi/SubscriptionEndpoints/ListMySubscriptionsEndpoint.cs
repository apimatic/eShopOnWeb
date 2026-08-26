using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.DTOs;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions with plan, price, state and next billing date.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal claims, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                var username = claims.Identity?.Name ?? string.Empty;
                return await HandleAsync(username, billingService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billingService)
    {
        return HandleAsync(string.Empty, billingService, CancellationToken.None);
    }

    private async Task<IResult> HandleAsync(string username, ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        try
        {
            var response = new ListMySubscriptionsResponse();
            response.Subscriptions.AddRange(await billingService.ListSubscriptionsAsync(username, cancellationToken));
            return Results.Ok(response);
        }
        catch (BillingException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode);
        }
    }

    public class ListMySubscriptionsResponse : BaseResponse
    {
        public List<CustomerSubscriptionDto> Subscriptions { get; set; } = new List<CustomerSubscriptionDto>();
    }
}
