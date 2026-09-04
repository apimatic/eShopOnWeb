using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Creates an idempotent subscription for the authenticated shopper.
/// </summary>
public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly IMaxioBillingClient _billingClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        IMaxioBillingClient billingClient, UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor)
    {
        _billingClient = billingClient;
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Microsoft.AspNetCore.Authorization.Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, HttpContext httpContext, CancellationToken cancellationToken) =>
                await HandleAsync(request, httpContext, cancellationToken))
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.PlanHandle)] = new[] { "A plan handle is required." }
            });
        }

        var user = await SubscriptionEndpointHelpers.GetCurrentUserAsync(httpContext, _userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var plans = await _billingClient.ListProductsAsync(cancellationToken);
        var plan = plans.SingleOrDefault(p => string.Equals(
            p.Handle, request.PlanHandle.Trim(), StringComparison.Ordinal));
        if (plan is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.PlanHandle)] = new[] { "The selected plan is not available." }
            });
        }

        var customerReference = SubscriptionReference.ForCustomer(user.Id);
        var customer = await _billingClient.CreateCustomerAsync(new MaxioCustomerAttributes
        {
            FirstName = "eShopOnWeb",
            LastName = "Customer",
            Email = user.Email ?? user.UserName ?? customerReference,
            Reference = customerReference
        }, cancellationToken);

        var subscription = await _billingClient.CreateSubscriptionAsync(new MaxioSubscriptionAttributes
        {
            ProductHandle = plan.Handle,
            CustomerReference = customer.Reference ?? customerReference,
            Reference = SubscriptionReference.ForSubscription(user.Id, plan.Handle),
            PaymentCollectionMethod = "remittance"
        }, cancellationToken);

        return Results.Ok(new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = SubscriptionDto.From(subscription)
        });
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request) =>
        HandleAsync(request, _httpContextAccessor.HttpContext!, CancellationToken.None);
}
