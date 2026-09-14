using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Returns the authenticated caller's subscriptions as reported by the billing system of record.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly ISubscriptionBillingService _billing;
    private readonly UserManager<ApplicationUser> _users;

    public ListMySubscriptionsEndpoint(ISubscriptionBillingService billing, UserManager<ApplicationUser> users)
    {
        _billing = billing;
        _users = users;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user) => await HandleAsync(user))
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "Lists my subscriptions",
                description: "Returns the authenticated caller's subscriptions from Maxio."));
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var identity = await SubscriberIdentity.ResolveAsync(user, _users);
        if (identity is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await _billing.GetSubscriptionsAsync(identity);
            var response = new ListMySubscriptionsResponse();
            response.Subscriptions.AddRange(subscriptions.Select(s => s.ToDto()));
            return Results.Ok(response);
        }
        catch (BillingServiceException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway,
                title: "Billing system unavailable");
        }
    }
}
