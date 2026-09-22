using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the current shopper to a plan (idempotent — a repeated call returns the existing
/// subscription rather than creating a second one).
/// POST /api/subscriptions (JWT authenticated).
/// </summary>
public class SubscribeEndpoint
    : IEndpoint<IResult, SubscribeSubscriptionRequest, ISubscriptionBillingService, CancellationToken>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    // IHttpContextAccessor is a singleton, so it is safe to inject into an endpoint constructed at
    // startup; the scoped ISubscriptionBillingService is resolved per request via the route lambda.
    public SubscribeEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeSubscriptionRequest request, ISubscriptionBillingService billing, CancellationToken ct) =>
                await HandleAsync(request, billing, ct))
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeSubscriptionRequest request,
        ISubscriptionBillingService billing, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
            return Results.BadRequest(new SubscriptionApiSupport.ErrorMessage("A planHandle is required."));

        var customer = SubscriptionApiSupport.BuildCustomer(_httpContextAccessor.HttpContext?.User);
        if (customer is null)
            return SubscriptionApiSupport.Unauthenticated();

        try
        {
            var details = await billing.SubscribeAsync(customer, request.PlanHandle!, ct);
            var dto = SubscriptionApiSupport.ToDto(details);
            return Results.Created("/api/my-subscriptions", dto);
        }
        catch (BillingException ex)
        {
            return SubscriptionApiSupport.ToErrorResult(ex);
        }
    }
}
