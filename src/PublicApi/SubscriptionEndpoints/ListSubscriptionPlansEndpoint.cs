using System;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans offered for the configured product family
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billing, CancellationToken ct) =>
            {
                var response = new ListSubscriptionPlansResponse(Guid.NewGuid());
                try
                {
                    var list = await billing.ListPlansAsync(ct);
                    response.IsComplete = list.IsComplete;
                    response.Plans = list.Plans.Select(p => new SubscriptionPlanDto
                    {
                        Handle = p.Handle,
                        Name = p.Name,
                        Description = p.Description,
                        Price = p.Price,
                        IntervalCount = p.IntervalCount,
                        IntervalUnit = p.IntervalUnit,
                        TrialOffered = p.TrialOffered,
                        SetupFee = p.SetupFee,
                        RequiresPaymentMethod = p.RequiresPaymentMethod
                    }).ToList();
                    return Results.Ok(response);
                }
                catch (BillingProviderException)
                {
                    return CallerIdentity.Fail(StatusCodes.Status503ServiceUnavailable, "The billing system is unavailable. Try again shortly.");
                }
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
