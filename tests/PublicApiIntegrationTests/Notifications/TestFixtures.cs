using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Notifications;
using Microsoft.eShopWeb.PublicApi.Sms;
using Microsoft.Extensions.Options;

namespace PublicApiIntegrationTests.Notifications;

/// <summary>
/// An in-memory <see cref="ISmsGateway"/> that records what it was asked to do and returns
/// deterministic results, so the notification logic can be tested without touching Twilio.
/// </summary>
public sealed class FakeSmsGateway : ISmsGateway
{
    public bool ValidationUsable { get; set; } = true;
    public string ValidationCanonical { get; set; } = "+15145550123";
    public bool ThrowOnSend { get; set; }
    public string SendStatus { get; set; } = DeliveryStatuses.Queued;

    public List<(string To, string Body)> Sent { get; } = new();
    public List<(string To, string Body, DateTimeOffset SendAt)> Scheduled { get; } = new();
    public List<string> Canceled { get; } = new();
    public List<string> Redacted { get; } = new();
    public List<string> Fetched { get; } = new();
    public List<ProviderMessageRecord> ProviderList { get; } = new();

    private int _sid;
    private readonly Dictionary<string, string> _statusBySid = new();
    private string NextSid() => $"SM{++_sid:D6}";

    public Task<PhoneValidationResult> ValidateDestinationAsync(string rawNumber, CancellationToken ct) =>
        Task.FromResult(new PhoneValidationResult(
            ValidationUsable,
            ValidationUsable ? ValidationCanonical : null,
            ValidationUsable ? null : "The number is not a usable SMS destination."));

    public Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken ct)
    {
        if (ThrowOnSend)
        {
            throw new SmsGatewayException("The messaging provider was unreachable.");
        }

        var sid = NextSid();
        _statusBySid[sid] = SendStatus;
        Sent.Add((toE164, body));
        return Task.FromResult(new SmsSendResult(sid, SendStatus, null, null, DateTimeOffset.UtcNow));
    }

    public Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct)
    {
        var sid = NextSid();
        _statusBySid[sid] = DeliveryStatuses.Scheduled;
        Scheduled.Add((toE164, body, sendAt));
        return Task.FromResult(new SmsSendResult(sid, DeliveryStatuses.Scheduled, null, null, null));
    }

    public Task<SmsSendResult> CancelScheduledAsync(string providerMessageSid, CancellationToken ct)
    {
        _statusBySid[providerMessageSid] = DeliveryStatuses.Canceled;
        Canceled.Add(providerMessageSid);
        return Task.FromResult(new SmsSendResult(providerMessageSid, DeliveryStatuses.Canceled, null, null, null));
    }

    public Task<SmsSendResult> FetchAsync(string providerMessageSid, CancellationToken ct)
    {
        Fetched.Add(providerMessageSid);
        // A real provider reports the message's own current status (e.g. a scheduled message stays
        // "scheduled" until it sends), not a single global status.
        var status = _statusBySid.TryGetValue(providerMessageSid, out var s) ? s : SendStatus;
        return Task.FromResult(new SmsSendResult(providerMessageSid, status, null, null, DateTimeOffset.UtcNow));
    }

    public Task RedactContentAsync(string providerMessageSid, CancellationToken ct)
    {
        Redacted.Add(providerMessageSid);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProviderMessageRecord>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Task.FromResult((IReadOnlyList<ProviderMessageRecord>)ProviderList);
}

public sealed class NullAppLogger<T> : IAppLogger<T>
{
    public void LogInformation(string message, params object[] args) { }
    public void LogWarning(string message, params object[] args) { }
}

/// <summary>Builds services over a fresh in-memory database, shared across a test.</summary>
public sealed class NotificationTestHarness
{
    public CatalogContext Context { get; }
    public FakeSmsGateway Gateway { get; } = new();
    public IRepository<ContactNumber> ContactNumbers { get; }
    public IRepository<Notification> Notifications { get; }
    public IRepository<Order> Orders { get; }
    public IRepository<CatalogItem> CatalogItems { get; }
    public ContactNumberService ContactNumberService { get; }
    public OrderNotificationService OrderNotificationService { get; }

    public NotificationTestHarness()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase("notif-tests-" + Guid.NewGuid())
            .Options;
        Context = new CatalogContext(options);

        ContactNumbers = new EfRepository<ContactNumber>(Context);
        Notifications = new EfRepository<Notification>(Context);
        Orders = new EfRepository<Order>(Context);
        CatalogItems = new EfRepository<CatalogItem>(Context);

        var uriComposer = new UriComposer(new CatalogSettings { CatalogBaseUrl = "" });
        var settings = Options.Create(new TwilioSettings
        {
            AccountSid = "ACtest",
            AuthToken = "test",
            FromNumber = "+15005550006",
            MessagingServiceSid = "MGtest",
            FollowUpDelayDays = 3
        });

        ContactNumberService = new ContactNumberService(ContactNumbers, Gateway, new NullAppLogger<ContactNumberService>());
        OrderNotificationService = new OrderNotificationService(
            Orders, Notifications, ContactNumbers, CatalogItems, uriComposer, Gateway, settings, new NullAppLogger<OrderNotificationService>());
    }

    public async Task<int> AddCatalogItemAsync(decimal price = 9.99m)
    {
        var item = new CatalogItem(2, 1, "test description", "test item", price, "pic.png");
        await CatalogItems.AddAsync(item);
        return item.Id;
    }
}
