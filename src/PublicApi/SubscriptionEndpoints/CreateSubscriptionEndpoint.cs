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

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, IMaxioService maxioService) =>
            {
                return await HandleAsync(request, maxioService);
            })
            .Produces<CreateSubscriptionRequest.Response>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioService maxioService)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new UnauthorizedAccessException("No HTTP context available");

        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue(ClaimTypes.Name)
            ?? throw new UnauthorizedAccessException("User identity not found");

        var email = httpContext.User.FindFirstValue(ClaimTypes.Email) ?? $"{userId}@placeholder.local";
        var firstName = httpContext.User.FindFirstValue("given_name") ?? "Subscriber";
        var lastName = httpContext.User.FindFirstValue("family_name") ?? "User";

        var response = new CreateSubscriptionRequest.Response(request.CorrelationId());

        var result = await maxioService.SubscribeAsync(userId, email, firstName, lastName, request.ProductHandle);
        response.Subscription = result;

        return Results.Created("/api/my-subscriptions", response);
    }
}
