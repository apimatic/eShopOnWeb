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

public class ListMySubscriptionsRequest : BaseMessage
{
}

public class MySubscriptionListEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, MaxioService>
{
    private readonly MaxioService _maxioService;

    public MySubscriptionListEndpoint(MaxioService maxioService)
    {
        _maxioService = maxioService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (MaxioService maxioService, HttpContext httpContext) =>
            {
                return await HandleAsync(new ListMySubscriptionsRequest(), maxioService, httpContext);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, MaxioService maxioService)
    {
        throw new NotSupportedException("Use the HttpContext overload.");
    }

    private async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, MaxioService maxioService, HttpContext httpContext)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var userId = httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User?.FindFirst("sub")?.Value
            ?? httpContext.User?.Identity?.Name;

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await maxioService.GetMySubscriptionsAsync(userId);
            response.Subscriptions = subscriptions.Select(s => new MySubscriptionDto
            {
                SubscriptionId = s.SubscriptionId,
                State = s.State,
                ProductName = s.ProductName,
                ProductHandle = s.ProductHandle,
                PriceInCents = s.PriceInCents,
                PriceInDollars = s.PriceInDollars,
                NextBillingAt = s.NextBillingAt,
                ActivatedAt = s.ActivatedAt,
                CreatedAt = s.CreatedAt,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt
            }).ToList();
            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(ex.Message, statusCode: 502);
        }
    }
}
