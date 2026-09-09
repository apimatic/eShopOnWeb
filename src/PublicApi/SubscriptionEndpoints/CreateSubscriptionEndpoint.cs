using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated shopper in a subscription plan. Idempotent per user and plan:
/// a repeated call never creates a second Maxio customer or a second live subscription.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    /// <summary>
    /// States that represent a live billing obligation; a re-subscribe while one of these
    /// exists returns the existing subscription instead of creating a new one.
    /// </summary>
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "past_due", "on_hold", "unpaid", "pending", "trialing", "trial_ended"
    };

    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _maxioOptions;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(IMaxioClient maxioClient,
        IOptions<MaxioOptions> maxioOptions,
        UserManager<ApplicationUser> userManager,
        ILogger<CreateSubscriptionEndpoint> logger)
    {
        _maxioClient = maxioClient;
        _maxioOptions = maxioOptions.Value;
        _userManager = userManager;
        _logger = logger;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the authenticated user to a plan",
        Description = "Ensures a Maxio customer exists for the user, then creates a subscription. Omit planHandle to use the default plan of the configured product family.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse();

        var username = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(username))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            return Unauthorized();
        }

        var gate = UserLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            MaxioSubscription subscription;
            bool created;

            try
            {
                var product = await ResolveTargetProductAsync(request, cancellationToken);
                (subscription, created) = await EnsureSubscribedAsync(user, product, cancellationToken);
            }
            catch (MaxioConfigurationException ex)
            {
                return ex.ToActionResult();
            }
            catch (MaxioApiException ex)
            {
                return ex.ToActionResult();
            }

            response.Subscription = SubscriptionMapper.ToSubscriptionDto(subscription);

            if (created)
            {
                _logger.LogInformation("User {UserId} subscribed to plan {PlanHandle} (Maxio subscription {SubscriptionId})",
                    user.Id, subscription.Product?.Handle, subscription.Id);
                return Created($"api/my-subscriptions", response);
            }

            return Ok(response);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<MaxioProduct> ResolveTargetProductAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            var requested = await _maxioClient.GetProductByHandleAsync(request.PlanHandle.Trim(), cancellationToken);

            if (requested is null)
            {
                throw new UnknownPlanException($"Unknown subscription plan '{request.PlanHandle}'.");
            }

            if (!string.Equals(requested.ProductFamily?.Handle, _maxioOptions.ProductFamilyHandle?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new UnknownPlanException($"Plan '{request.PlanHandle}' is not part of the configured product family.");
            }

            return requested;
        }

        var plans = await ListFamilyPlansAsync(cancellationToken);

        if (plans.Count == 0)
        {
            throw new UnknownPlanException("No subscription plans are configured for this site.");
        }

        return plans.OrderByDescending(p => p.PriceInCents).First();
    }

    private async Task<IReadOnlyList<MaxioProduct>> ListFamilyPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxioClient.ListProductsAsync(cancellationToken);
        return products
            .Where(p => string.Equals(p.ProductFamily?.Handle, _maxioOptions.ProductFamilyHandle?.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task<(MaxioSubscription subscription, bool created)> EnsureSubscribedAsync(ApplicationUser user, MaxioProduct product, CancellationToken cancellationToken)
    {
        var reference = user.Id;
        var customer = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);

        if (customer is null)
        {
            var (firstName, lastName) = DeriveName(user);
            customer = await _maxioClient.CreateCustomerAsync(reference, firstName, lastName, user.Email ?? user.UserName ?? string.Empty, cancellationToken);
        }

        var existingSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = existingSubscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, product.Handle, StringComparison.OrdinalIgnoreCase) &&
            !TerminalStates.Contains(s.State ?? string.Empty));

        if (existing is not null)
        {
            return (existing, false);
        }

        var createdSubscription = await _maxioClient.CreateSubscriptionAsync(customer.Id, product.Handle!, cancellationToken);
        return (createdSubscription, true);
    }

    private static (string firstName, string lastName) DeriveName(ApplicationUser user)
    {
        var localPart = (user.Email ?? user.UserName ?? "eshop shopper").Split('@')[0];
        var tokens = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        var firstName = tokens.Length > 0 ? Capitalize(tokens[0]) : "eShop";
        var lastName = tokens.Length > 1 ? Capitalize(tokens[^1]) : "Shopper";
        return (firstName, lastName);
    }

    private static string Capitalize(string value)
    {
        return string.Create(value.Length, value, (span, source) =>
        {
            source.AsSpan().CopyTo(span);
            if (span.Length > 0)
            {
                span[0] = char.ToUpperInvariant(span[0]);
            }
        });
    }

    /// <summary>
    /// Caller error surfaced while resolving the requested plan; mapped to 400/404 responses.
    /// </summary>
    private class UnknownPlanException : Exception
    {
        public UnknownPlanException(string message) : base(message)
        {
        }
    }
}
