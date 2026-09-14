using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.MaxioBilling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a subscription plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, IMaxioBillingService>
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly MaxioBillingOptions _options;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(IOptions<MaxioBillingOptions> options, ILogger<CreateSubscriptionEndpoint> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext http, IMaxioBillingService billingService) =>
            {
                return await HandleAsync(request, http.User, billingService);
            })
            .Accepts<CreateSubscriptionRequest>("application/json")
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioBillingService billingService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return SubscriptionEndpointErrors.Problem(StatusCodes.Status400BadRequest, "A planHandle is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            return SubscriptionEndpointErrors.Problem(
                StatusCodes.Status503ServiceUnavailable,
                "The Maxio product family is not configured. Set the Maxio:ProductFamilyHandle setting.");
        }

        var email = ResolveEmail(user);
        if (email is null)
        {
            return Results.Unauthorized();
        }

        string planHandle = request.PlanHandle.Trim();
        string reference = email;
        string lockKey = $"{reference}|{planHandle}";
        var semaphore = EnrollmentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync();
        try
        {
            return await EnrollAsync(response, email, planHandle, billingService);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<IResult> EnrollAsync(CreateSubscriptionResponse response, string email, string planHandle, IMaxioBillingService billingService)
    {
        try
        {
            var product = await billingService.FindProductByHandleAsync(planHandle, CancellationToken.None);
            if (product is null)
            {
                return SubscriptionEndpointErrors.Problem(
                    StatusCodes.Status404NotFound,
                    $"No subscription plan with handle '{planHandle}' was found.");
            }

            if (!string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            {
                return SubscriptionEndpointErrors.Problem(
                    StatusCodes.Status400BadRequest,
                    $"The plan '{planHandle}' is not part of the '{_options.ProductFamilyHandle}' product family and cannot be subscribed to here.");
            }

            var (firstName, lastName) = SplitName(email);

            var customer = await billingService.EnsureCustomerAsync(email, email, firstName, lastName, CancellationToken.None);
            _logger.LogInformation(
                "Ensured Maxio customer id {CustomerId} for eShop shopper '{Email}' (reference '{Reference}').",
                customer.Id, email, email);

            var existingSubscriptions = await billingService.ListCustomerSubscriptionsAsync(customer.Id, CancellationToken.None);
            var currentSubscription = existingSubscriptions.FirstOrDefault(subscription =>
                string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
                IsCurrentSubscriptionState(subscription.State));

            if (currentSubscription is not null)
            {
                response.Subscription = SubscriptionDto.FromMaxio(currentSubscription);
                response.Message = "You are already subscribed to this plan.";
                _logger.LogInformation(
                    "Shopper '{Email}' is already subscribed to plan '{PlanHandle}' via Maxio subscription {SubscriptionId}; returning the existing subscription.",
                    email, planHandle, currentSubscription.Id);
                return Results.Ok(response);
            }

            var created = await billingService.CreateSubscriptionAsync(customer.Id, planHandle, CancellationToken.None);
            response.Subscription = SubscriptionDto.FromMaxio(created);
            response.Message = "Subscription created.";
            return Results.Json(response, statusCode: StatusCodes.Status201Created);
        }
        catch (MaxioApiException ex)
        {
            _logger.LogError(ex, "Maxio failed to create the subscription for shopper '{Email}' to plan '{PlanHandle}'.", email, planHandle);

            if (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
            {
                var fallback = await FindCurrentSubscriptionAsync(email, planHandle, billingService);
                if (fallback is not null)
                {
                    response.Subscription = SubscriptionDto.FromMaxio(fallback);
                    response.Message = "You are already subscribed to this plan.";
                    return Results.Ok(response);
                }
            }

            return SubscriptionEndpointErrors.ToResult(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe shopper '{Email}' to plan '{PlanHandle}'.", email, planHandle);
            return SubscriptionEndpointErrors.ToResult(ex);
        }
    }

    private async Task<MaxioSubscription?> FindCurrentSubscriptionAsync(string email, string planHandle, IMaxioBillingService billingService)
    {
        var customer = await billingService.FindCustomerByReferenceAsync(email, CancellationToken.None);
        if (customer is null)
        {
            return null;
        }

        var subscriptions = await billingService.ListCustomerSubscriptionsAsync(customer.Id, CancellationToken.None);
        return subscriptions.FirstOrDefault(subscription =>
            string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            IsCurrentSubscriptionState(subscription.State));
    }

    private static string? ResolveEmail(ClaimsPrincipal user)
    {
        string? email = user.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrWhiteSpace(email))
        {
            email = user.Identity?.Name;
        }

        return string.IsNullOrWhiteSpace(email) ? null : email;
    }

    private static (string FirstName, string LastName) SplitName(string email)
    {
        int atIndex = email.IndexOf('@');
        string first = atIndex > 0 ? email[..atIndex] : email;
        string last = atIndex > 0 && atIndex < email.Length - 1 ? email[(atIndex + 1)..] : "Shopper";

        return (string.IsNullOrWhiteSpace(first) ? "eShop" : first, string.IsNullOrWhiteSpace(last) ? "Shopper" : last);
    }

    private static bool IsCurrentSubscriptionState(string? state)
    {
        return state switch
        {
            "active" or "trialing" or "past_due" or "unpaid" or "soft_failure" or "on_hold" or "suspended" or "awaiting_signup" => true,
            _ => false
        };
    }
}
