using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : AuthenticatedBillingRequest
{
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

/// <summary>
/// List the calling shopper's subscriptions, read live from Maxio
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext context, ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                var request = new ListMySubscriptionsRequest
                {
                    UserName = context.User.Identity?.Name ?? string.Empty,
                    RequestAborted = cancellationToken
                };
                return await HandleAsync(request, billing);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces<BillingErrorResponse>(StatusCodes.Status502BadGateway)
            .Produces<BillingErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionBillingService billing)
    {
        try
        {
            var subscriptions = await billing.GetSubscriptionsAsync(request.UserName, request.RequestAborted);
            return Results.Ok(new ListMySubscriptionsResponse(request.CorrelationId())
            {
                Subscriptions = subscriptions.Select(SubscriptionDto.From).ToList()
            });
        }
        catch (BillingException ex)
        {
            return BillingResults.From(ex, request.CorrelationId());
        }
    }
}
