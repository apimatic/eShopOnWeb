using System.Collections.Concurrent;
using System.Collections.Generic;
using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSubscriptionService
{
    private const int PageSize = 100;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly AppIdentityDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        MaxioSettings settings,
        AppIdentityDbContext db,
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _db = db;
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanResponse>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        using var callBudget = CreateCallBudget(cancellationToken);
        cancellationToken = callBudget.Token;
        var products = await LoadProductsAsync(cancellationToken);
        return products
            .Where(x => !string.IsNullOrWhiteSpace(x.Handle))
            .Select(x => new SubscriptionPlanResponse(
                x.Handle!,
                x.Name ?? x.Handle!,
                x.PriceInCents,
                ToInt32(x.Interval),
                x.IntervalUnit?.Value,
                x.ProductPricePointHandle,
                ToInt32(x.ProductPricePointId),
                x.Taxable))
            .ToArray();
    }

    public async Task<SubscriptionResponse> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        using var callBudget = CreateCallBudget(cancellationToken);
        cancellationToken = callBudget.Token;
        var shopper = await ResolveShopperAsync(cancellationToken);
        var planHandle = request.PlanHandle?.Trim();
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new MaxioProviderException("A subscription plan is required.", HttpStatusCode.BadRequest);

        var plan = (await LoadProductsAsync(cancellationToken))
            .SingleOrDefault(x => string.Equals(x.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null || string.IsNullOrWhiteSpace(plan.Handle))
            throw new MaxioProviderException("The selected subscription plan is not available.", HttpStatusCode.BadRequest);

        var pricePointHandle = request.ProductPricePointHandle?.Trim();
        if (!string.IsNullOrWhiteSpace(pricePointHandle) &&
            !string.Equals(pricePointHandle, plan.ProductPricePointHandle, StringComparison.OrdinalIgnoreCase))
            throw new MaxioProviderException("The selected subscription price is not available.", HttpStatusCode.BadRequest);

        var lockKey = $"subscription:{shopper.UserId}:{plan.Handle.ToLowerInvariant()}";
        var gate = Gates.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(shopper, createIfMissing: true, cancellationToken);
            if (customer is null)
                throw new MaxioProviderException("Billing customer could not be created.", HttpStatusCode.BadGateway);
            var reference = BuildSubscriptionReference(shopper.UserId, plan.Handle);
            var existing = await FindSubscriptionAsync(reference, cancellationToken);
            if (existing is not null)
                return await SaveAndMapAsync(shopper.UserId, plan.Handle, reference, existing, cancellationToken);

            SubscriptionResponse response;
            using (MaxioWriteAttemptScope.Begin())
            {
                try
                {
                    var body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            ProductPricePointHandle = pricePointHandle ?? plan.ProductPricePointHandle,
                            CustomerReference = customer.Reference,
                            Reference = reference,
                            PaymentCollectionMethod = CollectionMethod.Invoice
                        }
                    };
                    var created = await _client.Subscriptions.CreateSubscription(body, ct: cancellationToken);
                    response = RequireSubscription(created);
                }
                catch (MaxioWriteAttemptBlockedException ex)
                {
                    _logger.LogWarning(ex, "Maxio subscription write had an uncertain outcome for {Reference}", reference);
                    var reconciled = await FindSubscriptionAsync(reference, cancellationToken);
                    if (reconciled is null)
                        throw new MaxioProviderException("Subscription enrollment outcome could not be confirmed.", HttpStatusCode.BadGateway, ex);
                    response = reconciled;
                }
                catch (SdkException<CreateSubscriptionError> ex)
                {
                    var reconciled = await FindSubscriptionAsync(reference, cancellationToken);
                    if (reconciled is not null)
                    {
                        response = reconciled;
                    }
                    else if (ex.Error.TryGetErrorListResponse1(out var validation))
                    {
                        _logger.LogWarning("Maxio rejected subscription enrollment {Reference}: {Errors}", reference, string.Join("; ", validation.Errors));
                        throw new MaxioProviderException("Maxio rejected the subscription enrollment.", HttpStatusCode.UnprocessableEntity, ex);
                    }
                    else if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw ProviderFailure("Maxio rejected the subscription enrollment.", raw.StatusCode, ex);
                    }
                    else
                    {
                        throw new MaxioProviderException("Maxio rejected the subscription enrollment.", HttpStatusCode.BadGateway, ex);
                    }
                }
                catch (JsonException ex)
                {
                    throw new MaxioProviderException("Maxio returned a response that could not be processed.", HttpStatusCode.BadGateway, ex);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    throw new MaxioProviderException("Maxio is temporarily unavailable.", HttpStatusCode.BadGateway, ex);
                }
            }

            return await SaveAndMapAsync(shopper.UserId, plan.Handle, reference, response, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionResponse>> GetMySubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        using var callBudget = CreateCallBudget(cancellationToken);
        cancellationToken = callBudget.Token;
        var shopper = await ResolveShopperAsync(cancellationToken);
        var customer = await EnsureCustomerAsync(shopper, createIfMissing: false, cancellationToken);
        if (customer is null)
            return Array.Empty<SubscriptionResponse>();

        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customer.MaxioCustomerId, ct: cancellationToken);
            return subscriptions
                .Where(x => x.Subscription is not null)
                .Select(x => MapSubscription(x.Subscription!, null))
                .ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure("Unable to load your subscriptions.", ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioProviderException("Maxio returned a response that could not be processed.", HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioProviderException("Maxio is temporarily unavailable.", HttpStatusCode.BadGateway, ex);
        }
    }

    private async Task<IReadOnlyList<Product>> LoadProductsAsync(CancellationToken cancellationToken)
    {
        var families = await ListFamiliesAsync(cancellationToken);
        var family = families
            .Select(x => x.ProductFamily)
            .FirstOrDefault(x => string.Equals(x?.Handle, _settings.ProductFamilyHandle, StringComparison.Ordinal));
        var familyId = ToInt32(family?.Id);
        if (familyId is null)
            throw new MaxioProviderException("The configured subscription catalog could not be found.", HttpStatusCode.NotFound);

        var all = new List<Product>();
        var page = 1;
        while (true)
        {
            var pageItems = await ListProductsAsync(familyId.Value.ToString(CultureInfo.InvariantCulture), page, cancellationToken);
            all.AddRange(pageItems.Select(x => x.Product));
            if (pageItems.Count < PageSize)
                return all;
            page++;
        }
    }

    private async Task<IReadOnlyList<ProductFamilyResponse>> ListFamiliesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure("Unable to load subscription plans.", ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioProviderException("Maxio returned a response that could not be processed.", HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioProviderException("Maxio is temporarily unavailable.", HttpStatusCode.BadGateway, ex);
        }
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(string familyId, int page, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: page,
                perPage: PageSize,
                ct: cancellationToken);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
                throw new MaxioProviderException("The configured subscription catalog could not be found.", HttpStatusCode.NotFound, ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw ProviderFailure("Unable to load subscription plans.", raw.StatusCode, ex);
            throw new MaxioProviderException("Unable to load subscription plans.", HttpStatusCode.BadGateway, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioProviderException("Maxio returned a response that could not be processed.", HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioProviderException("Maxio is temporarily unavailable.", HttpStatusCode.BadGateway, ex);
        }
    }

    private async Task<MaxioCustomer?> EnsureCustomerAsync(Shopper shopper, bool createIfMissing, CancellationToken cancellationToken)
    {
        var gate = Gates.GetOrAdd($"customer:{shopper.UserId}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var reference = BuildCustomerReference(shopper.UserId);
            var found = await ReadCustomerAsync(reference, cancellationToken);
            if (found is not null)
                return await PersistCustomerAsync(shopper.UserId, reference, found, cancellationToken);
            if (!createIfMissing)
                return null;

            Customer customer;
            using (MaxioWriteAttemptScope.Begin())
            {
                try
                {
                    var body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = "eShopOnWeb",
                            LastName = shopper.DisplayName,
                            Email = shopper.Email,
                            Reference = reference
                        }
                    };
                    var created = await _client.Customers.CreateCustomer(body, ct: cancellationToken);
                    customer = RequireCustomer(created);
                }
                catch (MaxioWriteAttemptBlockedException ex)
                {
                    var reconciled = await ReadCustomerAsync(reference, cancellationToken);
                    if (reconciled is null)
                        throw new MaxioProviderException("Customer creation outcome could not be confirmed.", HttpStatusCode.BadGateway, ex);
                    customer = reconciled;
                }
                catch (SdkException<CreateCustomerError> ex)
                {
                    var reconciled = await ReadCustomerAsync(reference, cancellationToken);
                    if (reconciled is not null)
                    {
                        customer = reconciled;
                    }
                    else if (ex.Error.TryGetCustomerErrorResponse1(out _))
                    {
                        throw new MaxioProviderException("Maxio rejected customer creation.", HttpStatusCode.UnprocessableEntity, ex);
                    }
                    else if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw ProviderFailure("Unable to create your billing customer.", raw.StatusCode, ex);
                    }
                    else
                    {
                        throw new MaxioProviderException("Unable to create your billing customer.", HttpStatusCode.BadGateway, ex);
                    }
                }
                catch (JsonException ex)
                {
                    throw new MaxioProviderException("Maxio returned a response that could not be processed.", HttpStatusCode.BadGateway, ex);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    throw new MaxioProviderException("Maxio is temporarily unavailable.", HttpStatusCode.BadGateway, ex);
                }
            }

            return await PersistCustomerAsync(shopper.UserId, reference, customer, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Customer?> ReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _client.Customers.ReadCustomerByReference(reference, ct: cancellationToken);
            return result.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure("Unable to load your billing customer.", ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioProviderException("Maxio returned a response that could not be processed.", HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioProviderException("Maxio is temporarily unavailable.", HttpStatusCode.BadGateway, ex);
        }
    }

    private async Task<SubscriptionResponse?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _client.Subscriptions.FindSubscription(reference, ct: cancellationToken);
            return result.Subscription is null ? null : MapSubscription(result.Subscription, null);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
                return null;
            if (ex.Error.TryGetRawError(out var raw))
                throw ProviderFailure("Unable to check your subscription.", raw.StatusCode, ex);
            throw new MaxioProviderException("Unable to check your subscription.", HttpStatusCode.BadGateway, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioProviderException("Maxio returned a response that could not be processed.", HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioProviderException("Maxio is temporarily unavailable.", HttpStatusCode.BadGateway, ex);
        }
    }

    private async Task<SubscriptionResponse> SaveAndMapAsync(string userId, string planHandle, string reference, SubscriptionResponse subscription, CancellationToken cancellationToken)
    {
        var local = await _db.SubscriptionEnrollments.SingleOrDefaultAsync(x => x.UserId == userId && x.PlanHandle == planHandle, cancellationToken);
        if (local is null)
        {
            _db.SubscriptionEnrollments.Add(new SubscriptionEnrollment
            {
                UserId = userId,
                PlanHandle = planHandle,
                SubscriptionReference = reference,
                MaxioSubscriptionId = subscription.Id,
                LastSyncedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            local.MaxioSubscriptionId = subscription.Id;
            local.LastSyncedAt = DateTimeOffset.UtcNow;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            var winner = await _db.SubscriptionEnrollments.SingleOrDefaultAsync(x => x.UserId == userId && x.PlanHandle == planHandle, cancellationToken);
            if (winner is null)
                throw new MaxioProviderException("Subscription enrollment could not be recorded.", HttpStatusCode.Conflict, ex);
        }

        return subscription;
    }

    private async Task<MaxioCustomer> PersistCustomerAsync(string userId, string reference, Customer customer, CancellationToken cancellationToken)
    {
        var customerId = ToInt32(customer.Id);
        if (customerId is null)
            throw new MaxioProviderException("Maxio returned an invalid customer.", HttpStatusCode.BadGateway);

        var existing = await _db.MaxioCustomers.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (existing is null)
        {
            _db.MaxioCustomers.Add(new MaxioCustomer
            {
                UserId = userId,
                Reference = reference,
                MaxioCustomerId = customerId.Value
            });
        }
        else
        {
            existing.Reference = reference;
            existing.MaxioCustomerId = customerId.Value;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            var winner = await _db.MaxioCustomers.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
            if (winner is not null)
                return winner;
            throw new MaxioProviderException("Billing customer could not be recorded.", HttpStatusCode.Conflict, ex);
        }

        return await _db.MaxioCustomers.SingleAsync(x => x.UserId == userId, cancellationToken);
    }

    private async Task<Shopper> ResolveShopperAsync(CancellationToken cancellationToken)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            throw new MaxioProviderException("Authentication is required.", HttpStatusCode.Unauthorized);

        var claimId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        var claimName = principal.FindFirstValue(ClaimTypes.Name) ?? principal.FindFirstValue(ClaimTypes.Email);
        ApplicationUser? user = null;
        if (!string.IsNullOrWhiteSpace(claimId))
            user = await _userManager.FindByIdAsync(claimId);
        if (user is null && !string.IsNullOrWhiteSpace(claimName))
            user = await _userManager.FindByNameAsync(claimName);
        if (user is null)
            throw new MaxioProviderException("The authenticated account could not be found.", HttpStatusCode.Unauthorized);

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
            throw new MaxioProviderException("The authenticated account has no billing email.", HttpStatusCode.UnprocessableEntity);
        return new Shopper(user.Id, email, user.UserName ?? email);
    }

    private CancellationTokenSource CreateCallBudget(CancellationToken requestToken)
    {
        var hostToken = _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        var budget = CancellationTokenSource.CreateLinkedTokenSource(requestToken, hostToken);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        return budget;
    }

    private static Customer RequireCustomer(CustomerResponse response) =>
        response.Customer ?? throw new MaxioProviderException("Maxio returned no customer.", HttpStatusCode.BadGateway);

    private static SubscriptionResponse RequireSubscription(MaxioAdvancedBilling.Models.SubscriptionResponse response) =>
        response.Subscription is null
            ? throw new MaxioProviderException("Maxio returned no subscription.", HttpStatusCode.BadGateway)
            : MapSubscription(response.Subscription, null);

    private static SubscriptionResponse MapSubscription(Subscription subscription, string? fallbackPlanHandle) => new(
        ToInt32(subscription.Id),
        subscription.Reference,
        subscription.Product?.Handle ?? fallbackPlanHandle,
        subscription.Product?.Name,
        subscription.ProductPriceInCents,
        subscription.CurrentBillingAmountInCents,
        subscription.State?.Value,
        subscription.CurrentPeriodEndsAt,
        subscription.Currency);

    private static int? ToInt32(double? value)
    {
        if (!value.HasValue)
            return null;

        var number = value.Value;
        if (double.IsNaN(number) ||
            double.IsInfinity(number) ||
            number != Math.Truncate(number) ||
            number < int.MinValue ||
            number > int.MaxValue)
            return null;

        return checked((int)number);
    }

    private static string BuildCustomerReference(string userId) => $"eshop-customer-{userId}";

    private static string BuildSubscriptionReference(string userId, string planHandle)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{userId}|{planHandle}"));
        return $"eshop-subscription-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static MaxioProviderException ProviderFailure(string message, HttpStatusCode statusCode, Exception inner) =>
        new(message, statusCode, inner);

    private sealed record Shopper(string UserId, string Email, string DisplayName);
}
