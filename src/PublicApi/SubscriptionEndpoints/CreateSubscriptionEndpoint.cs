using System;
using System.Security.Claims;
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
/// Create a new subscription
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionEndpointRequest>
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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionEndpointRequest request) =>
            {
                return await HandleAsync(request);
            })
            .Produces<CreateSubscriptionEndpointResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionEndpointRequest request)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No active HTTP context.");

        var userId = httpContext.User.FindFirstValue(ClaimTypes.Name)
            ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? request.UserEmail;

        var firstName = httpContext.User.FindFirstValue(ClaimTypes.GivenName) ?? "User";
        var lastName = httpContext.User.FindFirstValue(ClaimTypes.Surname) ?? "Unknown";
        var email = httpContext.User.FindFirstValue(ClaimTypes.Email) ?? request.UserEmail ?? userId ?? "unknown@example.com";

        var result = await _subscriptionService.CreateSubscriptionAsync(
            email,
            firstName,
            lastName,
            request.ProductHandle,
            customerReference: userId);

        var response = new CreateSubscriptionEndpointResponse(request.CorrelationId())
        {
            Subscription = result,
        };

        return Results.Created($"/api/my-subscriptions", response);
    }
}
