using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, MaxioService>
{
    private readonly MaxioService _maxioService;

    public CreateSubscriptionEndpoint(MaxioService maxioService)
    {
        _maxioService = maxioService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, MaxioService maxioService, HttpContext httpContext) =>
            {
                return await HandleAsync(request, maxioService, httpContext);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, MaxioService maxioService)
    {
        throw new NotSupportedException("Use the HttpContext overload.");
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, MaxioService maxioService, HttpContext httpContext)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var userId = httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User?.FindFirst("sub")?.Value
            ?? httpContext.User?.Identity?.Name;

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        var email = httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? $"{userId}@placeholder.local";
        var firstName = httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value ?? "User";
        var lastName = httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.Surname)?.Value ?? userId;

        try
        {
            var result = await maxioService.SubscribeAsync(userId, email, firstName, lastName, request.ProductHandle);
            response.Subscription = new SubscriptionResultDto
            {
                SubscriptionId = result.SubscriptionId,
                State = result.State,
                ProductName = result.ProductName,
                ProductHandle = result.ProductHandle,
                PriceInCents = result.PriceInCents,
                PriceInDollars = result.PriceInDollars,
                NextBillingAt = result.NextBillingAt,
                ActivatedAt = result.ActivatedAt,
                CreatedAt = result.CreatedAt
            };
            return Results.Created($"api/my-subscriptions", response);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(ex.Message, statusCode: 400);
        }
    }
}
