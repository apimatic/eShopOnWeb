using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// GET /api/my-subscriptions — lists the authenticated shopper's subscriptions.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
                await HandleAsync(httpContext.User, billingService, cancellationToken))
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "List my subscriptions",
                description: "Lists the subscriptions belonging to the authenticated user."));
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionBillingService billingService,
        CancellationToken cancellationToken = default)
    {
        var reference = SubscriptionCaller.GetReference(user);
        var subscriptions = await billingService.GetSubscriptionsAsync(reference, cancellationToken);

        var response = new MySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(s => s.ToDto()).ToList()
        };

        return Results.Ok(response);
    }
}

public class MySubscriptionsResponse : BaseResponse
{
    public List<CustomerSubscriptionDto> Subscriptions { get; set; } = new();
}
