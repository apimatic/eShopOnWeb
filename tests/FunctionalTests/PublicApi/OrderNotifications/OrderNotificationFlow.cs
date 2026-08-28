using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.FunctionalTests.Web.Api;
using Microsoft.eShopWeb.PublicApi.OrderNotifications;
using Microsoft.eShopWeb.PublicApi.Twilio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Microsoft.eShopWeb.FunctionalTests.PublicApi.OrderNotifications;

[Collection("Sequential")]
public sealed class OrderNotificationFlow : IClassFixture<NotificationApiApplication>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly NotificationApiApplication _factory;
    private readonly HttpClient _client;

    public OrderNotificationFlow(NotificationApiApplication factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task DrivesOwnedOrderLifecycleAndOperatorActions()
    {
        var unauthenticated = await _client.GetAsync("/api/contact-numbers");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        Authorize(ApiTokenHelper.GetNormalUserToken());
        var contactResponse = await _client.PostAsJsonAsync("/api/contact-numbers",
            new RegisterContactNumberRequest { PhoneNumber = "approved-test-destination" });
        Assert.Equal(HttpStatusCode.Created, contactResponse.StatusCode);
        var contact = await ReadAsync<ContactNumberCreatedResponse>(contactResponse);
        Assert.True(contact.ContactNumberId > 0);

        var orderResponse = await _client.PostAsJsonAsync("/api/orders", new PlaceOrderRequest
        {
            Items = new() { new PlaceOrderLineRequest { CatalogItemId = 1, Quantity = 2 } },
            ShippingAddress = new ShippingAddressRequest
            {
                Street = "1 Test Way", City = "Toronto", State = "ON", Country = "Canada", ZipCode = "A1A 1A1"
            }
        });
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        var createdOrder = await ReadAsync<OrderCreatedResponse>(orderResponse);

        var notificationResponse = await _client.GetAsync($"/api/orders/{createdOrder.OrderId}/notifications");
        var originalMessages = await ReadAsync<List<NotificationResponse>>(notificationResponse);
        var failedPlaced = Assert.Single(originalMessages);
        Assert.Equal("undelivered", failedPlaced.DeliveryStatus);
        Assert.True(failedPlaced.NotificationId > 0);

        Authorize(ApiTokenHelper.GetAdminUserToken());
        var hiddenFromOtherShopper = await _client.GetAsync($"/api/orders/{createdOrder.OrderId}/notifications");
        Assert.Equal(HttpStatusCode.NotFound, hiddenFromOtherShopper.StatusCode);

        var firstResend = await _client.PostAsJsonAsync($"/api/notifications/{failedPlaced.NotificationId}/resend",
            new ResendNotificationRequest { IdempotencyKey = "same-operation" });
        Assert.Equal(HttpStatusCode.Created, firstResend.StatusCode);
        var firstResendBody = await ReadAsync<NotificationCreatedResponse>(firstResend);
        var sendsAfterFirstResend = _factory.Gateway.SendCalls;

        var repeatedResend = await _client.PostAsJsonAsync($"/api/notifications/{failedPlaced.NotificationId}/resend",
            new ResendNotificationRequest { IdempotencyKey = "same-operation" });
        var repeatedResendBody = await ReadAsync<NotificationCreatedResponse>(repeatedResend);
        Assert.Equal(firstResendBody.NotificationId, repeatedResendBody.NotificationId);
        Assert.Equal(sendsAfterFirstResend, _factory.Gateway.SendCalls);

        var freshResend = await _client.PostAsJsonAsync($"/api/notifications/{failedPlaced.NotificationId}/resend",
            new ResendNotificationRequest { IdempotencyKey = "fresh-operation" });
        var freshResendBody = await ReadAsync<NotificationCreatedResponse>(freshResend);
        Assert.NotEqual(firstResendBody.NotificationId, freshResendBody.NotificationId);
        Assert.Equal(sendsAfterFirstResend + 1, _factory.Gateway.SendCalls);

        var dispatch = await _client.PostAsync($"/api/orders/{createdOrder.OrderId}/dispatch", null);
        Assert.Equal(HttpStatusCode.OK, dispatch.StatusCode);
        Assert.Single(_factory.Gateway.ScheduledMessages);

        Authorize(ApiTokenHelper.GetNormalUserToken());
        var delete = await _client.DeleteAsync($"/api/contact-numbers/{contact.ContactNumberId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        var sendCountBeforeCancel = _factory.Gateway.SendCalls;

        Authorize(ApiTokenHelper.GetAdminUserToken());
        var cancel = await _client.PostAsync($"/api/orders/{createdOrder.OrderId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(sendCountBeforeCancel, _factory.Gateway.SendCalls);
        Assert.All(_factory.Gateway.ScheduledMessages, x => Assert.Equal("canceled", x.Status));

        var dispose = await _client.DeleteAsync($"/api/notifications/{failedPlaced.NotificationId}/content");
        Assert.Equal(HttpStatusCode.NoContent, dispose.StatusCode);
        Assert.Contains(failedPlaced.ProviderMessageId!, _factory.Gateway.RedactedMessageIds);

        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow.AddHours(1);
        var reconciliation = await _client.GetAsync($"/api/notifications/reconciliation?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}");
        var report = await ReadAsync<ReconciliationResponse>(reconciliation);
        Assert.True(report.ProviderCount > 0);
        Assert.Contains(report.Entries, x => x.Presence == "both");
        Assert.Equal(from, _factory.Gateway.LastListFrom);
        Assert.Equal(to, _factory.Gateway.LastListTo);

        Authorize(ApiTokenHelper.GetNormalUserToken());
        var finalMessagesResponse = await _client.GetAsync($"/api/orders/{createdOrder.OrderId}/notifications");
        var finalMessages = await ReadAsync<List<NotificationResponse>>(finalMessagesResponse);
        var disposed = Assert.Single(finalMessages.Where(x => x.NotificationId == failedPlaced.NotificationId));
        Assert.True(disposed.ContentDisposed);
        Assert.Null(disposed.Content);

        var contactsResponse = await _client.GetAsync("/api/contact-numbers");
        Assert.Empty(await ReadAsync<List<ContactNumberResponse>>(contactsResponse));
    }

    private void Authorize(string token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }
}

public sealed class NotificationApiApplication : WebApplicationFactory<global::Program>
{
    public FakeTwilioGateway Gateway { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("UseOnlyInMemoryDatabase", "true");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITwilioGateway>();
            services.AddSingleton<ITwilioGateway>(Gateway);
        });
    }
}

public sealed class FakeTwilioGateway : ITwilioGateway
{
    private readonly ConcurrentDictionary<string, MutableMessage> _messages = new();
    private int _nextId;

    public int SendCalls { get; private set; }
    public List<MutableMessage> ScheduledMessages { get; } = new();
    public HashSet<string> RedactedMessageIds { get; } = new();
    public DateTimeOffset? LastListFrom { get; private set; }
    public DateTimeOffset? LastListTo { get; private set; }

    public Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string input, CancellationToken cancellationToken) =>
        Task.FromResult(new ValidatedPhoneNumber("approved-test-destination", true));

    public Task<TwilioMessage> SendMessageAsync(string to, string body, CancellationToken cancellationToken)
    {
        SendCalls++;
        var status = SendCalls == 1 ? "undelivered" : "delivered";
        return Task.FromResult(Create(body, to, status, null));
    }

    public Task<TwilioMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var message = CreateMutable(body, to, "scheduled", sendAt);
        ScheduledMessages.Add(message);
        return Task.FromResult(message.ToRecord());
    }

    public Task<TwilioMessage> FetchMessageAsync(string sid, CancellationToken cancellationToken) =>
        Task.FromResult(_messages[sid].ToRecord());

    public Task<TwilioMessage> CancelMessageAsync(string sid, CancellationToken cancellationToken)
    {
        var message = _messages[sid];
        message.Status = "canceled";
        message.Updated = DateTimeOffset.UtcNow;
        return Task.FromResult(message.ToRecord());
    }

    public Task<TwilioMessage> RedactMessageAsync(string sid, CancellationToken cancellationToken)
    {
        var message = _messages[sid];
        message.Body = string.Empty;
        message.Updated = DateTimeOffset.UtcNow;
        RedactedMessageIds.Add(sid);
        return Task.FromResult(message.ToRecord());
    }

    public Task<IReadOnlyList<TwilioMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        LastListFrom = from;
        LastListTo = to;
        return Task.FromResult<IReadOnlyList<TwilioMessage>>(_messages.Values
            .Where(x => x.Created >= from && x.Created <= to).Select(x => x.ToRecord()).ToList());
    }

    private TwilioMessage Create(string body, string to, string status, DateTimeOffset? scheduledFor) =>
        CreateMutable(body, to, status, scheduledFor).ToRecord();

    private MutableMessage CreateMutable(string body, string to, string status, DateTimeOffset? scheduledFor)
    {
        var id = Interlocked.Increment(ref _nextId).ToString("D32");
        var message = new MutableMessage
        {
            Sid = "SM" + id,
            Body = body,
            To = to,
            Status = status,
            Created = DateTimeOffset.UtcNow,
            Updated = DateTimeOffset.UtcNow,
            DateSent = scheduledFor is null ? DateTimeOffset.UtcNow : null
        };
        _messages[message.Sid] = message;
        return message;
    }

    public sealed class MutableMessage
    {
        public string Sid { get; init; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Body { get; set; }
        public string To { get; init; } = string.Empty;
        public DateTimeOffset Created { get; init; }
        public DateTimeOffset Updated { get; set; }
        public DateTimeOffset? DateSent { get; init; }

        public TwilioMessage ToRecord() => new(Sid, Status, Body, "approved-sender", To, null,
            Created, DateSent, Updated);
    }
}
