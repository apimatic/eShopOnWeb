using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Builds a real <see cref="SubscriptionService"/> over a substituted billing seam, so the domain
/// rules can be exercised without a transport. The billing client is the only thing faked here.
/// </summary>
public sealed class SubscriptionServiceHarness
{
    public const string UserName = "demouser@microsoft.com";

    public SubscriptionServiceHarness()
    {
        BillingClient = Substitute.For<IBillingClient>();
        Publisher = Substitute.For<IPublisher>();

        var catalogSettings = Substitute.For<ISubscriptionCatalogSettings>();
        catalogSettings.ProductFamilyHandle.Returns(MaxioTestHarness.ProductFamilyHandle);
        catalogSettings.DefaultPlanHandle.Returns(MaxioTestHarness.DefaultPlanHandle);
        catalogSettings.AlternatePlanHandle.Returns(MaxioTestHarness.AlternatePlanHandle);
        catalogSettings.MeteredComponentHandle.Returns(MaxioTestHarness.MeteredComponentHandle);
        CatalogSettings = catalogSettings;

        Service = new SubscriptionService(
            BillingClient,
            CatalogSettings,
            Publisher,
            Substitute.For<IAppLogger<SubscriptionService>>());
    }

    public IBillingClient BillingClient { get; }

    public IPublisher Publisher { get; }

    public ISubscriptionCatalogSettings CatalogSettings { get; }

    public ISubscriptionService Service { get; }

    /// <summary>Notifications the service published, in order.</summary>
    public IReadOnlyList<INotification> PublishedNotifications =>
        Publisher.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IPublisher.Publish))
            .Select(c => c.GetArguments()[0])
            .OfType<INotification>()
            .ToList();

    public static SubscriptionPlan Plan(
        string handle = MaxioTestHarness.DefaultPlanHandle,
        decimal price = 299.00m,
        bool archived = false) => new()
        {
            Id = 7130997,
            Handle = handle,
            Name = handle == MaxioTestHarness.DefaultPlanHandle ? "Pro Plan" : "Basic Plan",
            Price = price,
            Interval = 1,
            IntervalUnit = "month",
            ProductFamilyHandle = MaxioTestHarness.ProductFamilyHandle,
            IsArchived = archived
        };

    public static BillingCustomer Customer(int id = 55001) => new()
    {
        Id = id,
        Reference = UserName,
        Email = UserName
    };

    public static Subscription Sub(
        int id = 88001,
        SubscriptionState state = SubscriptionState.Active,
        string planHandle = MaxioTestHarness.DefaultPlanHandle) => new()
        {
            Id = id,
            State = state,
            CustomerId = 55001,
            CustomerReference = UserName,
            PlanHandle = planHandle,
            PlanName = "Pro Plan",
            PlanPrice = 299.00m,
            CurrentPeriodEndsAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)
        };

    public static MeteredComponent Component(
        bool isMetered = true,
        bool archived = false,
        string familyHandle = MaxioTestHarness.ProductFamilyHandle) => new()
        {
            Id = 3062733,
            Handle = MaxioTestHarness.MeteredComponentHandle,
            Name = "API Calls",
            IsMetered = isMetered,
            Kind = isMetered ? "metered_component" : "quantity_based_component",
            UnitPrice = 0.01m,
            ProductFamilyHandle = familyHandle,
            IsArchived = archived
        };

    public static UsageRecord Usage(decimal quantity = 1m) => new()
    {
        Id = 991001,
        SubscriptionId = 88001,
        ComponentId = 3062733,
        ComponentHandle = MaxioTestHarness.MeteredComponentHandle,
        Quantity = quantity
    };

    public static PlanChangePreview Preview(
        int subscriptionId = 88001,
        string current = MaxioTestHarness.DefaultPlanHandle,
        string target = MaxioTestHarness.AlternatePlanHandle,
        PlanChangeTiming timing = PlanChangeTiming.Immediate,
        decimal paymentDue = 50.00m) => new()
        {
            SubscriptionId = subscriptionId,
            CurrentPlanHandle = current,
            TargetPlanHandle = target,
            Timing = timing,
            ProratedAdjustment = -249.00m,
            Charge = 29.00m,
            PaymentDue = paymentDue,
            CreditApplied = 249.00m
        };
}
