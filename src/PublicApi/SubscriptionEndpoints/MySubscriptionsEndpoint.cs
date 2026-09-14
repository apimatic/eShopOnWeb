using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's own subscriptions.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal,
             UserManager<ApplicationUser> userManager,
             ISubscriptionBillingService billingService,
             CancellationToken cancellationToken) =>
            {
                var identity = await SubscriberIdentity.ResolveAsync(principal, userManager);
                if (identity is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new MySubscriptionsRequest(identity.Reference), billingService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    // Interface member (MinimalApi.Endpoint) — delegates to the cancellation-aware overload.
    public Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionBillingService billingService)
        => HandleAsync(request, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        var subscriptions = await billingService.GetSubscriptionsAsync(request.UserReference, cancellationToken);

        var response = new ListMySubscriptionsResponse(request.CorrelationId())
        {
            Subscriptions = subscriptions.Select(CustomerSubscriptionDto.FromDomain).ToList()
        };

        return Results.Ok(response);
    }
}

/// <summary>
/// Internal request for the my-subscriptions endpoint. The user reference is
/// resolved from the caller's token, never from the request body.
/// </summary>
public class MySubscriptionsRequest : BaseRequest
{
    public MySubscriptionsRequest(string userReference)
    {
        UserReference = userReference;
    }

    public string UserReference { get; }
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId) { }

    public ListMySubscriptionsResponse() { }

    public List<CustomerSubscriptionDto> Subscriptions { get; set; } = new();
}
