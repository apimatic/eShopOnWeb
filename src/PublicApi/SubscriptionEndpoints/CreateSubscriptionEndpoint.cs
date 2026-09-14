using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: a repeated call with the same
/// plan replays the existing subscription instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>, IMaxioBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> userManager, IMaxioBillingService billingService) =>
            {
                return await HandleAsync(request, userManager, billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> userManager, IMaxioBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.Problem(statusCode: 400, title: "ProductHandle is required.");
        }

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext is available for the subscription request.");
        var (subscriber, problem) = await BillingIdentity.ResolveAsync(httpContext, userManager);
        if (subscriber is null)
        {
            return problem!;
        }

        try
        {
            var result = await billingService.SubscribeAsync(subscriber, request.ProductHandle);
            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = ToDto(result.Subscription),
                CreatedNew = result.CreatedNew,
            };

            return result.CreatedNew
                ? Results.Created("api/my-subscriptions", response)
                : Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return MaxioBillingHttpResults.From(ex);
        }
    }

    internal static SubscriptionDto ToDto(MaxioSubscriptionInfo info) =>
        new()
        {
            SubscriptionId = info.SubscriptionId,
            PlanHandle = info.PlanHandle,
            PlanName = info.PlanName,
            PriceInCents = info.PriceInCents,
            Interval = info.Interval,
            IntervalUnit = info.IntervalUnit,
            State = info.State,
            NextBillingAt = info.NextBillingAt,
        };
}
