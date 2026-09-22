using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the current shopper's subscriptions.
/// GET /api/my-subscriptions (JWT authenticated).
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ISubscriptionBillingService, CancellationToken>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsListEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billing, CancellationToken ct) =>
                await HandleAsync(billing, ct))
            .Produces<IEnumerable<SubscriptionDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billing, CancellationToken ct)
    {
        var customer = SubscriptionApiSupport.BuildCustomer(_httpContextAccessor.HttpContext?.User);
        if (customer is null)
            return SubscriptionApiSupport.Unauthenticated();

        try
        {
            var subscriptions = await billing.GetSubscriptionsAsync(customer, ct);
            return Results.Ok(subscriptions.Select(SubscriptionApiSupport.ToDto).ToList());
        }
        catch (BillingException ex)
        {
            return SubscriptionApiSupport.ToErrorResult(ex);
        }
    }
}
