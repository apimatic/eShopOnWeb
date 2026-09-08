using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.MaxioBilling;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the Maxio subscriptions belonging to the authenticated shopper.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, IMaxioBillingService>
{
    private readonly ILogger<MySubscriptionsEndpoint> _logger;

    public MySubscriptionsEndpoint(ILogger<MySubscriptionsEndpoint> logger)
    {
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IMaxioBillingService billingService) =>
            {
                return await HandleAsync(http.User, billingService);
            })
            .Produces<MySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IMaxioBillingService billingService)
    {
        var response = new MySubscriptionsResponse();

        string? email = user.FindFirst(ClaimTypes.Email)?.Value ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(email))
        {
            return Results.Unauthorized();
        }

        response.Shopper = email;

        try
        {
            var customer = await billingService.FindCustomerByReferenceAsync(email, CancellationToken.None);
            if (customer is null)
            {
                return Results.Ok(response);
            }

            var subscriptions = await billingService.ListCustomerSubscriptionsAsync(customer.Id, CancellationToken.None);

            response.Subscriptions.AddRange(subscriptions
                .OrderByDescending(subscription => subscription.CreatedAt)
                .Select(SubscriptionDto.FromMaxio));

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list Maxio subscriptions for shopper '{Email}'.", email);
            return SubscriptionEndpointErrors.ToResult(ex);
        }
    }
}
