using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.FunctionalTests.Web.Api;
using Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Microsoft.eShopWeb.FunctionalTests.PublicApi.OrderNotificationEndpoints;

public sealed class OrderNotificationFlow
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task DrivesTheCompleteFlowWithOwnershipAndIdempotency()
    {
        var provider = new FakeTextMessageProvider();
        var databaseName = $"OrderNotificationFlow-{Guid.NewGuid()}";
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["UseOnlyInMemoryDatabase"] = "true" }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITextMessageProvider>();
                services.AddSingleton<ITextMessageProvider>(provider);
                services.RemoveAll<DbContextOptions<CatalogContext>>();
                services.AddDbContext<CatalogContext>(options =>
                    options.UseInMemoryDatabase(databaseName));
            });
        });

        using var shopper = CreateClient(factory, ApiTokenHelper.GetNormalUserToken());
        using var administrator = CreateClient(factory, ApiTokenHelper.GetAdminUserToken());

        var contactResponse = await shopper.PostAsJsonAsync("api/contact-numbers", new
        {
            phoneNumber = "+10000000000"
        });
        Assert.Equal(HttpStatusCode.Created, contactResponse.StatusCode);
        var contact = await ReadAsync<RegisterContactNumberResponse>(contactResponse);

        var contacts = await shopper.GetFromJsonAsync<List<ContactNumberResponse>>("api/contact-numbers", JsonOptions);
        Assert.Single(contacts!);
        Assert.Equal(contact.ContactNumberId, contacts![0].ContactNumberId);

        var otherShopperDelete = await administrator.DeleteAsync($"api/contact-numbers/{contact.ContactNumberId}");
        Assert.Equal(HttpStatusCode.NotFound, otherShopperDelete.StatusCode);

        var orderResponse = await shopper.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } }
        });
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        var createdOrder = await ReadAsync<PlaceOrderResponse>(orderResponse);
        Assert.Single(provider.Messages);

        var shopperCannotDispatch = await shopper.PostAsync($"api/orders/{createdOrder.OrderId}/dispatch", null);
        Assert.Equal(HttpStatusCode.Forbidden, shopperCannotDispatch.StatusCode);

        var dispatch = await administrator.PostAsync($"api/orders/{createdOrder.OrderId}/dispatch", null);
        Assert.Equal(HttpStatusCode.OK, dispatch.StatusCode);
        Assert.Single(provider.Messages.Where(x => x.ScheduledFor.HasValue));

        var notificationsBeforeCancel = await shopper.GetFromJsonAsync<OrderNotificationsResponse>(
            $"api/orders/{createdOrder.OrderId}/notifications",
            JsonOptions);
        Assert.Equal(3, notificationsBeforeCancel!.Notifications.Count);
        var failed = notificationsBeforeCancel.Notifications.Single(x => x.Type == "OrderPlaced");

        var sendsBeforeResend = provider.SendCalls;
        var firstResendTask = administrator.PostAsJsonAsync(
            $"api/notifications/{failed.NotificationId}/resend",
            new { idempotencyKey = "same-operation" });
        var repeatedResendTask = administrator.PostAsJsonAsync(
            $"api/notifications/{failed.NotificationId}/resend",
            new { idempotencyKey = "same-operation" });
        await Task.WhenAll(firstResendTask, repeatedResendTask);
        var firstResendResponse = await firstResendTask;
        var repeatedResendResponse = await repeatedResendTask;
        Assert.Equal(HttpStatusCode.Created, firstResendResponse.StatusCode);
        var firstResend = await ReadAsync<ResendNotificationResponse>(firstResendResponse);
        var repeatedResend = await ReadAsync<ResendNotificationResponse>(repeatedResendResponse);
        Assert.Equal(firstResend.NotificationId, repeatedResend.NotificationId);
        Assert.Equal(sendsBeforeResend + 1, provider.SendCalls);

        var cancel = await administrator.PostAsync($"api/orders/{createdOrder.OrderId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.All(provider.Messages.Where(x => x.ScheduledFor.HasValue), x => Assert.Equal("canceled", x.Status));

        var dispose = await administrator.DeleteAsync($"api/notifications/{firstResend.NotificationId}/content");
        Assert.Equal(HttpStatusCode.NoContent, dispose.StatusCode);
        Assert.True(provider.Messages.Single(x => x.LocalNotificationHint == firstResend.NotificationId).ContentRedacted);

        var notificationsAfterDispose = await shopper.GetFromJsonAsync<OrderNotificationsResponse>(
            $"api/orders/{createdOrder.OrderId}/notifications",
            JsonOptions);
        var disposed = notificationsAfterDispose!.Notifications.Single(x => x.NotificationId == firstResend.NotificationId);
        Assert.True(disposed.ContentDisposed);
        Assert.Null(disposed.Content);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var reconciliation = await administrator.GetFromJsonAsync<ReconciliationResponse>(
            $"api/notifications/reconciliation?from={from}&to={to}",
            JsonOptions);
        Assert.Contains(reconciliation!.Entries, x => x.Match == "matched");

        var myOrders = await shopper.GetFromJsonAsync<MyOrdersResponse>("api/my-orders", JsonOptions);
        Assert.Equal("Cancelled", myOrders!.Orders.Single(x => x.OrderId == createdOrder.OrderId).Status);

        var remove = await shopper.DeleteAsync($"api/contact-numbers/{contact.ContactNumberId}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        contacts = await shopper.GetFromJsonAsync<List<ContactNumberResponse>>("api/contact-numbers", JsonOptions);
        Assert.Empty(contacts!);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;

    private sealed class FakeTextMessageProvider : ITextMessageProvider
    {
        private readonly List<FakeMessage> _messages = new();
        private int _sequence;

        public IReadOnlyList<FakeMessage> Messages => _messages;
        public int SendCalls { get; private set; }

        public Task<ValidatedDestination> ValidateDestinationAsync(string input, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ValidatedDestination(true, input, Array.Empty<string>()));

        public Task<ProviderMessage> SendAsync(
            string destination,
            string body,
            DateTimeOffset? sendAt = null,
            CancellationToken cancellationToken = default)
        {
            SendCalls++;
            var now = DateTimeOffset.UtcNow;
            var message = new FakeMessage
            {
                Sid = $"SM{++_sequence:D32}",
                Status = sendAt.HasValue ? "scheduled" : "undelivered",
                ErrorCode = sendAt.HasValue ? null : 30008,
                DateCreated = now,
                DateSent = sendAt.HasValue ? null : now,
                To = destination,
                ScheduledFor = sendAt,
                Body = body
            };
            _messages.Add(message);
            return Task.FromResult(Map(message));
        }

        public Task<ProviderMessage> GetAsync(string messageSid, CancellationToken cancellationToken = default) =>
            Task.FromResult(Map(Find(messageSid)));

        public Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default)
        {
            var message = Find(messageSid);
            message.Status = "canceled";
            return Task.FromResult(Map(message));
        }

        public Task<ProviderMessage> RedactContentAsync(string messageSid, CancellationToken cancellationToken = default)
        {
            var message = Find(messageSid);
            message.Body = string.Empty;
            message.ContentRedacted = true;
            return Task.FromResult(Map(message));
        }

        public Task<IReadOnlyList<ProviderMessage>> ListAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderMessage>>(_messages
                .Where(x => x.DateSent >= from && x.DateSent <= to)
                .Select(Map)
                .ToList());

        private FakeMessage Find(string sid) => _messages.Single(x => x.Sid == sid);

        private static ProviderMessage Map(FakeMessage message) => new(
            message.Sid,
            message.Status,
            message.ErrorCode,
            message.DateCreated,
            message.DateSent,
            message.To);

        public sealed class FakeMessage
        {
            public string Sid { get; init; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public int? ErrorCode { get; init; }
            public DateTimeOffset DateCreated { get; init; }
            public DateTimeOffset? DateSent { get; init; }
            public string To { get; init; } = string.Empty;
            public DateTimeOffset? ScheduledFor { get; init; }
            public string Body { get; set; } = string.Empty;
            public bool ContentRedacted { get; set; }
            public int LocalNotificationHint => int.Parse(Sid[^8..]);
        }
    }
}
