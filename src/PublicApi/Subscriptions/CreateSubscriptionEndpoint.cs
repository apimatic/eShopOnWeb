using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
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
            (CreateSubscriptionRequest request,
                ISubscriptionBillingService billing) =>
                await HandleAsync(request, billing))
            .Accepts<CreateSubscriptionRequest>("application/json")
            .Produces<SubscriptionDto>(StatusCodes.Status200OK)
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionBillingService billing)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var userResolver = httpContext?.RequestServices.GetRequiredService<IBillingUserResolver>() ??
            throw new BillingApiException(StatusCodes.Status401Unauthorized, "An authenticated user is required.");
        return HandleRequestAsync(
            request,
            billing,
            userResolver,
            httpContext.User,
            httpContext.RequestAborted);
    }

    private static async Task<IResult> HandleRequestAsync(
        CreateSubscriptionRequest request,
        ISubscriptionBillingService billing,
        IBillingUserResolver userResolver,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var user = await userResolver.ResolveAsync(principal);
        var result = await billing.SubscribeAsync(user, request.ProductHandle, cancellationToken);
        return result.Created
            ? Results.Created("/api/my-subscriptions", result.Subscription)
            : Results.Ok(result.Subscription);
    }
}
