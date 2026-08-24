using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans (Maxio products in the configured product family) available to subscribe to.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly SubscriptionBillingService _billingService;

    public ListSubscriptionPlansEndpoint(SubscriptionBillingService billingService)
    {
        _billingService = billingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    Task<IResult> IEndpoint<IResult, ClaimsPrincipal>.HandleAsync(ClaimsPrincipal user) =>
        HandleAsync(user, CancellationToken.None);

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        try
        {
            var response = new ListSubscriptionPlansResponse();
            response.Plans.AddRange(await _billingService.ListPlansAsync(cancellationToken));
            return Results.Ok(response);
        }
        catch (MaxioApiException ex)
        {
            return MaxioError(ex);
        }
    }

    internal static IResult MaxioError(MaxioApiException ex) =>
        Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway, title: "Maxio billing request failed");
}
