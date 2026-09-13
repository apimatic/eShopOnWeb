using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult, HttpContext>
{
    private readonly IMaxioApiClient _maxioClient;
    private readonly ILogger<MySubscriptionsEndpoint> _logger;

    public MySubscriptionsEndpoint(
        IMaxioApiClient maxioClient,
        ILogger<MySubscriptionsEndpoint> logger)
    {
        _maxioClient = maxioClient;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext) =>
            {
                return await HandleAsync(httpContext);
            })
            .RequireAuthorization(auth => auth.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser())
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        var userId = httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        _logger.LogInformation("Listing subscriptions for user {UserId}", userId);

        // Find the Maxio customer for this user
        var customer = await _maxioClient.FindCustomerByReferenceAsync(userId);
        if (customer == null)
        {
            _logger.LogInformation("No Maxio customer found for user {UserId}, returning empty list", userId);
            return Results.Ok(new MySubscriptionsResponse { Subscriptions = new List<SubscriptionDto>() });
        }

        var subscriptions = await _maxioClient.ListSubscriptionsForCustomerAsync(customer.Id);

        var response = new MySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                State = s.State,
                ProductHandle = s.ProductHandle,
                ProductName = s.Product?.Name ?? string.Empty,
                Price = s.ProductPriceInCents / 100m,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt,
                ActivatedAt = s.ActivatedAt,
                CreatedAt = s.CreatedAt,
                CanceledAt = s.CanceledAt,
                CancelAtEndOfPeriod = s.CancelAtEndOfPeriod,
                CustomerEmail = s.Customer?.Email ?? string.Empty
            }).ToList()
        };

        return Results.Ok(response);
    }
}
