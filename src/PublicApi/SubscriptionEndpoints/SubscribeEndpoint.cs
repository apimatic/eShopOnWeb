using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi;

public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscribeEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, IMaxioBillingService billingService) =>
            {
                return await HandleAsync(request, billingService);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioBillingService billingService)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var userId = httpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext?.User.FindFirstValue("sub")
            ?? httpContext?.User.Identity?.Name;

        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        if (string.IsNullOrEmpty(request.ProductHandle))
            return Results.BadRequest(new { error = "ProductHandle is required." });

        var email = httpContext?.User.FindFirstValue(ClaimTypes.Email) ?? $"{userId}@eshop.local";
        var firstName = httpContext?.User.FindFirstValue(ClaimTypes.GivenName) ?? "eShop";
        var lastName = httpContext?.User.FindFirstValue(ClaimTypes.Surname) ?? "Customer";

        var result = await billingService.SubscribeAsync(userId, email, firstName, lastName, request.ProductHandle);

        return Results.Ok(new SubscribeResponse { Subscription = result });
    }
}
