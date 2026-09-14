using System.Collections.Generic;
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

public sealed class MySubscriptionListEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionListEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billing) =>
                await HandleAsync(billing))
            .Produces<IReadOnlyList<SubscriptionDto>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billing)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var userResolver = httpContext?.RequestServices.GetRequiredService<IBillingUserResolver>() ??
            throw new BillingApiException(StatusCodes.Status401Unauthorized, "An authenticated user is required.");
        return HandleRequestAsync(billing, userResolver, httpContext.User, httpContext.RequestAborted);
    }

    private static async Task<IResult> HandleRequestAsync(
        ISubscriptionBillingService billing,
        IBillingUserResolver userResolver,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var user = await userResolver.ResolveAsync(principal);
        return Results.Ok(await billing.ListSubscriptionsAsync(user, cancellationToken));
    }
}
