using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService : ISubscriptionService
{
    private static readonly TimeSpan EnrollmentLease = TimeSpan.FromMinutes(2);
    private readonly IMaxioClient _maxioClient;
    private readonly AppIdentityDbContext _identityDbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SubscriptionKeyedLock _keyedLock;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioClient maxioClient,
        AppIdentityDbContext identityDbContext,
        UserManager<ApplicationUser> userManager,
        SubscriptionKeyedLock keyedLock,
        IOptions<MaxioOptions> options,
        ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _identityDbContext = identityDbContext;
        _userManager = userManager;
        _keyedLock = keyedLock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await ListConfiguredProductsAsync(cancellationToken);
        return products
            .OrderBy(x => x.PriceInCents)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        string userName,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var normalizedHandle = productHandle.Trim();
        var user = await _userManager.FindByNameAsync(userName) ?? throw new SubscriptionUserNotFoundException();

        await using var keyedLock = await _keyedLock.AcquireAsync(
            $"{user.Id}:{normalizedHandle.ToUpperInvariant()}",
            cancellationToken);

        var plans = await ListConfiguredProductsAsync(cancellationToken);
        var plan = plans.SingleOrDefault(x =>
            string.Equals(x.Handle, normalizedHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(normalizedHandle);
        }

        var customer = await EnsureCustomerAsync(user, cancellationToken);
        var existing = FindProductSubscription(
            await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken),
            plan.Handle);
        if (existing is not null)
        {
            await PersistCompletedEnrollmentAsync(user.Id, plan.Handle, customer.Id, existing.Id, cancellationToken);
            return MapSubscription(existing);
        }

        var (enrollment, ownsEnrollment) = await ClaimEnrollmentAsync(
            user.Id,
            plan.Handle,
            customer.Id,
            cancellationToken);
        if (!ownsEnrollment)
        {
            var completed = await WaitForEnrollmentAsync(enrollment, customer.Id, plan.Handle, cancellationToken);
            if (completed is not null)
            {
                return MapSubscription(completed);
            }

            throw new SubscriptionEnrollmentInProgressException();
        }

        try
        {
            var created = await _maxioClient.CreateSubscriptionAsync(
                new CreateMaxioSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerReference = user.Id,
                    Reference = CreateSubscriptionReference(user.Id, plan.Handle),
                    PaymentCollectionMethod = "remittance"
                },
                cancellationToken);

            await CompleteEnrollmentAsync(enrollment, customer.Id, created.Id, cancellationToken);
            return MapSubscription(created);
        }
        catch (Exception exception) when (IsReconciliationCandidate(exception, cancellationToken))
        {
            var reconciled = FindProductSubscription(
                await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken),
                plan.Handle);
            if (reconciled is not null)
            {
                await CompleteEnrollmentAsync(enrollment, customer.Id, reconciled.Id, cancellationToken);
                return MapSubscription(reconciled);
            }

            if (exception is MaxioApiException { IsTransient: false })
            {
                await MarkEnrollmentFailedAsync(enrollment, cancellationToken);
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(
        string userName,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByNameAsync(userName) ?? throw new SubscriptionUserNotFoundException();
        var customer = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var configuredHandles = (await ListConfiguredProductsAsync(cancellationToken))
            .Select(x => x.Handle)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        return subscriptions
            .Where(x => x.Product is not null && configuredHandles.Contains(x.Product.Handle))
            .OrderByDescending(x => x.CreatedAt)
            .Select(MapSubscription)
            .ToList();
    }

    private async Task<IReadOnlyList<MaxioProduct>> ListConfiguredProductsAsync(CancellationToken cancellationToken)
    {
        var products = await _maxioClient.ListProductsAsync(cancellationToken);
        return products
            .Where(x => x.ArchivedAt is null && string.Equals(
                x.ProductFamily.Handle,
                _options.ProductFamilyHandle,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var (firstName, lastName) = SplitCustomerName(user.Email ?? user.UserName ?? "eShop customer");
        try
        {
            return await _maxioClient.CreateCustomerAsync(
                new CreateMaxioCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = user.Email ?? user.UserName ?? throw new SubscriptionUserNotFoundException(),
                    Reference = user.Id
                },
                cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Customer reference uniqueness is Maxio's cross-instance idempotency guard.
            var concurrentlyCreated = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
            if (concurrentlyCreated is null)
            {
                throw;
            }

            return concurrentlyCreated;
        }
    }

    private async Task<(SubscriptionEnrollment Enrollment, bool OwnsEnrollment)> ClaimEnrollmentAsync(
        string userId,
        string productHandle,
        long customerId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var enrollment = await _identityDbContext.SubscriptionEnrollments.SingleOrDefaultAsync(
            x => x.UserId == userId && x.ProductHandle == productHandle,
            cancellationToken);

        if (enrollment is null)
        {
            enrollment = new SubscriptionEnrollment
            {
                UserId = userId,
                ProductHandle = productHandle,
                MaxioCustomerId = customerId,
                Status = SubscriptionEnrollmentStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now
            };
            _identityDbContext.SubscriptionEnrollments.Add(enrollment);
            try
            {
                await _identityDbContext.SaveChangesAsync(cancellationToken);
                return (enrollment, true);
            }
            catch (DbUpdateException)
            {
                _identityDbContext.ChangeTracker.Clear();
                var concurrentlyCreated = await _identityDbContext.SubscriptionEnrollments.SingleOrDefaultAsync(
                    x => x.UserId == userId && x.ProductHandle == productHandle,
                    cancellationToken);
                if (concurrentlyCreated is null)
                {
                    throw;
                }

                return (concurrentlyCreated, false);
            }
        }

        if (enrollment.Status == SubscriptionEnrollmentStatus.Pending &&
            now - enrollment.UpdatedAt < EnrollmentLease)
        {
            return (enrollment, false);
        }

        enrollment.Status = SubscriptionEnrollmentStatus.Pending;
        enrollment.MaxioCustomerId = customerId;
        enrollment.MaxioSubscriptionId = null;
        enrollment.OperationId = Guid.NewGuid().ToString("N");
        enrollment.UpdatedAt = now;
        enrollment.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        try
        {
            await _identityDbContext.SaveChangesAsync(cancellationToken);
            return (enrollment, true);
        }
        catch (DbUpdateConcurrencyException)
        {
            await _identityDbContext.Entry(enrollment).ReloadAsync(cancellationToken);
            return (enrollment, false);
        }
    }

    private async Task<MaxioSubscription?> WaitForEnrollmentAsync(
        SubscriptionEnrollment enrollment,
        long customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            await _identityDbContext.Entry(enrollment).ReloadAsync(cancellationToken);
            if (enrollment.Status == SubscriptionEnrollmentStatus.Completed)
            {
                break;
            }
        }

        return FindProductSubscription(
            await _maxioClient.ListCustomerSubscriptionsAsync(customerId, cancellationToken),
            productHandle);
    }

    private async Task PersistCompletedEnrollmentAsync(
        string userId,
        string productHandle,
        long customerId,
        long subscriptionId,
        CancellationToken cancellationToken)
    {
        var enrollment = await _identityDbContext.SubscriptionEnrollments.SingleOrDefaultAsync(
            x => x.UserId == userId && x.ProductHandle == productHandle,
            cancellationToken);
        if (enrollment is null)
        {
            enrollment = new SubscriptionEnrollment
            {
                UserId = userId,
                ProductHandle = productHandle,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _identityDbContext.SubscriptionEnrollments.Add(enrollment);
        }

        await CompleteEnrollmentAsync(enrollment, customerId, subscriptionId, cancellationToken);
    }

    private async Task CompleteEnrollmentAsync(
        SubscriptionEnrollment enrollment,
        long customerId,
        long subscriptionId,
        CancellationToken cancellationToken)
    {
        enrollment.MaxioCustomerId = customerId;
        enrollment.MaxioSubscriptionId = subscriptionId;
        enrollment.Status = SubscriptionEnrollmentStatus.Completed;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        enrollment.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        try
        {
            await _identityDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // The remote subscription is authoritative and future requests reconcile it.
            _logger.LogWarning(exception, "Could not persist the local Maxio enrollment mapping.");
        }
    }

    private async Task MarkEnrollmentFailedAsync(
        SubscriptionEnrollment enrollment,
        CancellationToken cancellationToken)
    {
        enrollment.Status = SubscriptionEnrollmentStatus.Failed;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        enrollment.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        await _identityDbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsReconciliationCandidate(Exception exception, CancellationToken cancellationToken) =>
        exception is MaxioApiException or HttpRequestException ||
        (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static MaxioSubscription? FindProductSubscription(
        IReadOnlyList<MaxioSubscription> subscriptions,
        string productHandle) =>
        subscriptions
            .Where(x => string.Equals(x.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();

    private static SubscriptionPlanDto MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit
    };

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription)
    {
        var product = subscription.Product ?? throw new InvalidOperationException(
            "Maxio returned a subscription without a product.");
        return new SubscriptionDto
        {
            SubscriptionId = subscription.Id,
            ProductHandle = product.Handle,
            ProductName = product.Name,
            PriceInCents = subscription.ProductPriceInCents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit,
            Currency = subscription.Currency,
            State = subscription.State,
            NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
        };
    }

    private static (string FirstName, string LastName) SplitCustomerName(string value)
    {
        var localPart = value.Split('@', 2)[0];
        var parts = localPart.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return ("eShop", "Customer");
        }

        return parts.Length == 1
            ? (parts[0], "Customer")
            : (parts[0], string.Join(' ', parts.Skip(1)));
    }

    private static string CreateSubscriptionReference(string userId, string productHandle)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{productHandle}"));
        return $"eshop-{Convert.ToHexString(bytes)[..32].ToLowerInvariant()}";
    }
}
