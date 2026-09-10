using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public MySubscriptionsResponse() { }
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

/// <summary>Lists the authenticated shopper's own subscriptions, as held by Maxio.</summary>
/// <remarks>
/// Scoped services are resolved per-request from <see cref="HttpContext.RequestServices"/> because
/// endpoints are singletons (see <see cref="SubscribeEndpoint"/>).
/// </remarks>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async () => await HandleAsync())
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext!;
        var services = httpContext.RequestServices;
        var billing = services.GetRequiredService<ISubscriptionBillingService>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        var subscriber = await SubscriberResolver.ResolveAsync(httpContext.User, userManager);
        var subscriptions = await billing.ListSubscriptionsAsync(subscriber, httpContext.RequestAborted);

        var response = new MySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(SubscriptionDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
