using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", HandleAsync)
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService service)
    {
        return HandleInternalAsync(request, service);
    }

    private static async Task<IResult> HandleInternalAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService service)
    {
        if (string.IsNullOrEmpty(request.ProductHandle))
        {
            return Results.BadRequest(new { error = "ProductHandle is required" });
        }

        var subscription = await service.CreateSubscriptionAsync("system-user", request.ProductHandle);
        var response = new CreateSubscriptionResponse
        {
            Subscription = subscription
        };
        return Results.Created($"api/subscriptions/{subscription.Id}", response);
    }

    private static string? GetUserIdFromContext(HttpContext httpContext)
    {
        var principal = httpContext.User;
        return principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
               principal?.FindFirst("sub")?.Value ??
               principal?.FindFirst(ClaimTypes.Email)?.Value;
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public SubscriptionDto Subscription { get; set; } = new();
}
