using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available to a logged-in shopper.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, ISubscriptionBillingService>
{
    private readonly ISubscriptionBillingService _billing;

    public ListSubscriptionPlansEndpoint(ISubscriptionBillingService billing)
    {
        _billing = billing;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            () =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), _billing);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, ISubscriptionBillingService billing)
    {
        try
        {
            var plans = await billing.ListPlansAsync();
            var response = new ListSubscriptionPlansResponse(request.CorrelationId());
            response.SubscriptionPlans.AddRange(plans.Select(p => new SubscriptionPlanDto
            {
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                Price = p.Price,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit,
                RequiresPaymentMethod = p.RequiresPaymentMethod
            }));
            return Results.Ok(response);
        }
        catch (ApplicationCore.Exceptions.MaxioBillingException ex)
        {
            return ex.ToProblemResult();
        }
    }
}
