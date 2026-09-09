using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions as recorded in the billing
/// system of record.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionBillingService, IHttpContextAccessor>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService, IHttpContextAccessor httpContextAccessor) =>
            {
                return await HandleAsync(new MySubscriptionsRequest(), billingService, httpContextAccessor);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        MySubscriptionsRequest request, ISubscriptionBillingService billingService, IHttpContextAccessor httpContextAccessor)
    {
        var userName = httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var result = await billingService.GetSubscriptionsForUserAsync(userName, CancellationToken.None);

        if (result.Status == ResultStatus.NotFound)
        {
            return Results.NotFound(new { correlationId = request.CorrelationId(), errors = result.Errors });
        }

        if (result.Status != ResultStatus.Ok)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Billing system error",
                detail: string.Join("; ", result.Errors));
        }

        var response = new MySubscriptionsResponse(request.CorrelationId());
        response.Subscriptions.AddRange(result.Value.Select(CreateSubscriptionEndpoint.ToDto));
        return Results.Ok(response);
    }
}
