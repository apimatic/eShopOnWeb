using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests.NotificationEndpoints;

/// <summary>
/// A <see cref="WebApplicationFactory{Program}"/> that swaps the live Twilio-backed provider clients for
/// in-memory fakes, so the notification flows can be exercised end-to-end without sending real messages.
/// </summary>
public class NotificationTestFactory : WebApplicationFactory<Program>
{
    public FakeSmsSender Sms { get; } = new();
    public FakePhoneNumberValidator Validator { get; } = new();

    // A unique database name per factory instance keeps each test's orders / contact numbers /
    // notifications isolated (the in-memory store is otherwise shared across factories in the process).
    private readonly string _catalogDb = "Catalog_" + Guid.NewGuid();
    private readonly string _identityDb = "Identity_" + Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISmsSender>();
            services.RemoveAll<IPhoneNumberValidator>();
            services.AddSingleton<ISmsSender>(Sms);
            services.AddSingleton<IPhoneNumberValidator>(Validator);

            services.RemoveAll<DbContextOptions<CatalogContext>>();
            services.AddDbContext<CatalogContext>(o => o.UseInMemoryDatabase(_catalogDb));

            services.RemoveAll<DbContextOptions<AppIdentityDbContext>>();
            services.AddDbContext<AppIdentityDbContext>(o => o.UseInMemoryDatabase(_identityDb));
        });
    }
}

/// <summary>An in-memory stand-in for the messaging provider that records everything it is asked to do.</summary>
public class FakeSmsSender : ISmsSender
{
    private readonly ConcurrentDictionary<string, StoredMessage> _messages = new();
    private int _counter;

    public string SendingNumber => "+15005550006";

    public int SendCount => _messages.Count;

    public IReadOnlyCollection<StoredMessage> Messages => _messages.Values.ToList();

    public Task<SmsMessage> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        var sid = "SM" + Interlocked.Increment(ref _counter).ToString("D32");
        var scheduled = request.ScheduleFor.HasValue;
        var stored = new StoredMessage
        {
            Sid = sid,
            To = request.To,
            From = SendingNumber,
            Body = request.Body,
            Status = scheduled ? "scheduled" : "queued",
            DateSent = scheduled ? null : DateTimeOffset.UtcNow,
            DateCreated = DateTimeOffset.UtcNow
        };
        _messages[sid] = stored;
        return Task.FromResult(stored.ToSmsMessage());
    }

    public Task<SmsMessage> GetAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        if (!_messages.TryGetValue(messageSid, out var stored))
        {
            throw new InvalidOperationException("Unknown message SID.");
        }
        return Task.FromResult(stored.ToSmsMessage());
    }

    public Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(messageSid, out var stored))
        {
            stored.Status = "canceled";
        }
        return Task.CompletedTask;
    }

    public Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(messageSid, out var stored))
        {
            stored.Body = string.Empty;
            stored.Redacted = true;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SmsMessage>> ListAsync(SmsListFilter filter, CancellationToken cancellationToken = default)
    {
        IEnumerable<StoredMessage> query = _messages.Values;
        if (!string.IsNullOrEmpty(filter.From))
        {
            query = query.Where(m => m.From == filter.From);
        }
        if (filter.DateSentAfter.HasValue)
        {
            query = query.Where(m => m.DateSent.HasValue && m.DateSent.Value >= filter.DateSentAfter.Value);
        }
        if (filter.DateSentBefore.HasValue)
        {
            query = query.Where(m => m.DateSent.HasValue && m.DateSent.Value <= filter.DateSentBefore.Value);
        }
        return Task.FromResult<IReadOnlyList<SmsMessage>>(query.Select(m => m.ToSmsMessage()).ToList());
    }

    public class StoredMessage
    {
        public string Sid { get; set; } = default!;
        public string To { get; set; } = default!;
        public string From { get; set; } = default!;
        public string Body { get; set; } = default!;
        public string Status { get; set; } = default!;
        public bool Redacted { get; set; }
        public DateTimeOffset? DateSent { get; set; }
        public DateTimeOffset? DateCreated { get; set; }

        public SmsMessage ToSmsMessage() => new()
        {
            Sid = Sid,
            Status = Status,
            From = From,
            To = To,
            Body = Body,
            DateSent = DateSent,
            DateCreated = DateCreated
        };
    }
}

/// <summary>Treats any number containing at least 10 digits as valid, returning a canonical E.164 form.</summary>
public class FakePhoneNumberValidator : IPhoneNumberValidator
{
    public Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var digits = new string((phoneNumber ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length < 10)
        {
            return Task.FromResult(new PhoneNumberValidationResult(false, null, new[] { "TOO_SHORT" }));
        }

        var canonical = "+" + digits;
        return Task.FromResult(new PhoneNumberValidationResult(true, canonical));
    }
}
