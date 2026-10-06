using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available from the configured Maxio product family
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, ISubscriptionBillingService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), billingService, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ListSubscriptionPlansRequest request,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        try
        {
            var plans = await billingService.ListPlansAsync(cancellationToken);
            response.Plans.AddRange(plans);
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionProblemResults.From(ex);
        }
    }
}
