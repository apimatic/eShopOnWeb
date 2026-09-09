using System;
using System.Linq;
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
/// Lists the subscription plans available to logged-in shoppers.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), billingService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, ISubscriptionBillingService billingService)
    {
        var result = await billingService.GetPlansAsync(CancellationToken.None);

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

        var response = new ListSubscriptionPlansResponse(request.CorrelationId());
        response.SubscriptionPlans.AddRange(result.Value
            .Select(p => new SubscriptionPlanDto
            {
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                PriceCents = p.PriceCents,
                Price = (p.PriceCents / 100m).ToString("F2"),
                BillingInterval = p.BillingInterval,
                RequiresPaymentMethod = p.RequiresPaymentMethod
            }));

        return Results.Ok(response);
    }
}
