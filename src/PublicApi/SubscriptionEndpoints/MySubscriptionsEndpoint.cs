using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, HttpContext>
{
    public Task<IResult> HandleAsync(HttpContext context) =>
        HandleAsync(context, context.RequestServices.GetRequiredService<ISubscriptionBillingService>(),
            context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>());

    private static async Task<IResult> HandleAsync(HttpContext context,
        ISubscriptionBillingService service, UserManager<ApplicationUser> userManager)
    {
        var user = await SubscriptionEndpointHelpers.GetCurrentUserAsync(context, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(new MySubscriptionsResponse
            {
                Subscriptions = await service.GetMySubscriptionsAsync(user, context.RequestAborted)
            });
        }
        catch (MaxioApiException)
        {
            return SubscriptionEndpointHelpers.MaxioFailure();
        }
        catch (HttpRequestException)
        {
            return SubscriptionEndpointHelpers.ServiceUnavailable();
        }
        catch (System.InvalidOperationException)
        {
            return SubscriptionEndpointHelpers.ServiceUnavailable();
        }
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (
                HttpContext context, ISubscriptionBillingService service) =>
            await HandleAsync(context, service, context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>()))
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}

public sealed class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public MySubscriptionsResponse() { }

    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = new List<SubscriptionDto>();
}
