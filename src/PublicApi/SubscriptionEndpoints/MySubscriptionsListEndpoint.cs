using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions as reflected by the billing system of record.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        // The second (CancellationToken) parameter forces binding to the Delegate overload of MapGet
        // (returns RouteHandlerBuilder, enabling .Produces) instead of the RequestDelegate overload.
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, System.Threading.CancellationToken cancellationToken) => await HandleAsync(http))
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Lists the current user's subscriptions"));
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        var subscriber = SubscriptionEndpointHelpers.BuildSubscriber(http.User);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var billingService = http.RequestServices.GetRequiredService<ISubscriptionBillingService>();
        try
        {
            var subscriptions = await billingService.GetMySubscriptionsAsync(subscriber, http.RequestAborted);
            var response = new ListMySubscriptionsResponse();
            response.Subscriptions.AddRange(subscriptions.Select(SubscriptionEndpointHelpers.ToDto));
            return Results.Ok(response);
        }
        catch (SubscriptionBillingException ex)
        {
            return SubscriptionEndpointHelpers.ToProblem(ex);
        }
    }
}

/// <summary>Response for <c>GET /api/my-subscriptions</c>.</summary>
public class ListMySubscriptionsResponse : BaseResponse
{
    public System.Collections.Generic.List<SubscriptionDto> Subscriptions { get; set; } = new();
}
