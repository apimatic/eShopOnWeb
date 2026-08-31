using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.UnitTests.Builders;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderNotificationServiceTests;

public class OrderNotificationServiceTests
{
    private const string BuyerId = "buyer@example.com";
    private const string CanonicalNumber = "+15551234567";

    private readonly IRepository<ContactNumber> _contactNumbers = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly IMessagingProvider _provider = Substitute.For<IMessagingProvider>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private OrderNotificationService CreateService()
    {
        return new OrderNotificationService(
            _contactNumbers,
            _notifications,
            _provider,
            Options.Create(new TwilioSettings { FollowUpDelayDays = 3 }),
            _logger);
    }

    // Simulates a persisted order (EF assigns the Id on save).
    private static Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order PersistedOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        typeof(Microsoft.eShopWeb.ApplicationCore.Entities.BaseEntity)
            .GetProperty(nameof(Microsoft.eShopWeb.ApplicationCore.Entities.BaseEntity.Id))!
            .SetValue(order, 1);
        return order;
    }

    [Fact]
    public async Task RegisterStoresProvidersCanonicalForm()
    {
        _provider.VerifyPhoneNumberAsync("(555) 123-4567", Arg.Any<CancellationToken>())
            .Returns(new VerifiedPhoneNumber(CanonicalNumber));
        _contactNumbers.ListAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());
        _contactNumbers.AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<ContactNumber>());

        var result = await CreateService().RegisterContactNumberAsync(BuyerId, "(555) 123-4567", CancellationToken.None);

        Assert.Equal(CanonicalNumber, result.PhoneNumber);
        await _contactNumbers.Received(1).AddAsync(
            Arg.Is<ContactNumber>(c => c.PhoneNumber == CanonicalNumber && c.BuyerId == BuyerId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterRejectsNumberProviderConsidersUnusable()
    {
        _provider.VerifyPhoneNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((VerifiedPhoneNumber?)null);

        await Assert.ThrowsAsync<PhoneNumberNotValidException>(
            () => CreateService().RegisterContactNumberAsync(BuyerId, "not-a-number", CancellationToken.None));

        await _contactNumbers.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyPlacedWithoutContactNumberSendsNothing()
    {
        _contactNumbers.ListAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());

        await CreateService().NotifyOrderPlacedAsync(new OrderBuilder().WithDefaultValues(), CancellationToken.None);

        await _provider.DidNotReceive().SendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyPlacedWhenProviderFailsDoesNotThrowAndRecordsFailure()
    {
        var order = PersistedOrder();
        _contactNumbers.ListAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { new ContactNumber(order.BuyerId, CanonicalNumber) });
        _notifications.AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<OrderNotification>());
        _provider.SendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MessagingProviderException("rejected", HttpStatusCode.BadRequest));

        await CreateService().NotifyOrderPlacedAsync(order, CancellationToken.None);

        await _notifications.Received().UpdateAsync(
            Arg.Is<OrderNotification>(n => n.ProviderStatus == "failed" && n.ProviderMessageSid == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyDispatchedSchedulesFollowUpWithProvider()
    {
        var order = PersistedOrder();
        _contactNumbers.ListAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { new ContactNumber(order.BuyerId, CanonicalNumber) });
        _notifications.AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<OrderNotification>());
        _provider.SendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderMessage("SM1", "queued", null, null, CanonicalNumber, "+1999", null));
        _provider.ScheduleMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderMessage("SM2", "scheduled", null, null, CanonicalNumber, null, null));

        await CreateService().NotifyOrderDispatchedAsync(order, CancellationToken.None);

        await _provider.Received(1).ScheduleMessageAsync(
            CanonicalNumber,
            Arg.Any<string>(),
            Arg.Is<DateTimeOffset>(d => d > DateTimeOffset.UtcNow.AddDays(2)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyCancelledCancelsPendingFollowUpAtProvider()
    {
        var order = PersistedOrder();
        var followUp = new OrderNotification(order.Id, order.BuyerId, CanonicalNumber,
            NotificationKind.DeliveryFollowUp, "follow-up", DateTimeOffset.UtcNow.AddDays(3));
        followUp.UpdateProviderState("SM-FOLLOWUP", "scheduled", null, null);

        _notifications.ListAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification> { followUp });
        _contactNumbers.ListAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());
        _provider.CancelScheduledMessageAsync("SM-FOLLOWUP", Arg.Any<CancellationToken>())
            .Returns(new ProviderMessage("SM-FOLLOWUP", "canceled", null, null, CanonicalNumber, null, null));

        await CreateService().NotifyOrderCancelledAsync(order, CancellationToken.None);

        await _provider.Received(1).CancelScheduledMessageAsync("SM-FOLLOWUP", Arg.Any<CancellationToken>());
        Assert.Equal("canceled", followUp.ProviderStatus);
    }

    [Fact]
    public async Task ResendUnderRepeatedKeyDoesNotSendSecondMessage()
    {
        var original = new OrderNotification(1, BuyerId, CanonicalNumber, NotificationKind.OrderPlaced, "body");
        var priorResend = new OrderNotification(1, BuyerId, CanonicalNumber, NotificationKind.Resend, "body",
            resendOfId: original.Id, idempotencyKey: "key-1");

        _notifications.GetByIdAsync(original.Id, Arg.Any<CancellationToken>()).Returns(original);
        _notifications.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns(priorResend);

        var result = await CreateService().ResendNotificationAsync(original.Id, "key-1", CancellationToken.None);

        Assert.True(result.IdempotentReplay);
        Assert.Same(priorResend, result.Notification);
        await _provider.DidNotReceive().SendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendUnderFreshKeySendsNewMessage()
    {
        var original = new OrderNotification(1, BuyerId, CanonicalNumber, NotificationKind.OrderPlaced, "body");
        _notifications.GetByIdAsync(original.Id, Arg.Any<CancellationToken>()).Returns(original);
        _notifications.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        _notifications.AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<OrderNotification>());
        _provider.SendMessageAsync(CanonicalNumber, "body", Arg.Any<CancellationToken>())
            .Returns(new ProviderMessage("SM-RESEND", "queued", null, null, CanonicalNumber, "+1999", null));

        var result = await CreateService().ResendNotificationAsync(original.Id, "key-2", CancellationToken.None);

        Assert.False(result.IdempotentReplay);
        Assert.Equal("SM-RESEND", result.Notification.ProviderMessageSid);
        await _provider.Received(1).SendMessageAsync(CanonicalNumber, "body", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendOfDisposedContentIsRejected()
    {
        var original = new OrderNotification(1, BuyerId, CanonicalNumber, NotificationKind.OrderPlaced, "body");
        original.MarkBodyRedacted();
        _notifications.GetByIdAsync(original.Id, Arg.Any<CancellationToken>()).Returns(original);
        _notifications.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);

        await Assert.ThrowsAsync<DomainRuleViolationException>(
            () => CreateService().ResendNotificationAsync(original.Id, "key-3", CancellationToken.None));
    }

    [Fact]
    public async Task RedactContentRedactsAtProviderAndLocally()
    {
        var notification = new OrderNotification(1, BuyerId, CanonicalNumber, NotificationKind.OrderPlaced, "body");
        notification.UpdateProviderState("SM1", "delivered", null, null);
        _notifications.GetByIdAsync(notification.Id, Arg.Any<CancellationToken>()).Returns(notification);

        await CreateService().RedactNotificationContentAsync(notification.Id, CancellationToken.None);

        await _provider.Received(1).RedactMessageBodyAsync("SM1", Arg.Any<CancellationToken>());
        Assert.True(notification.BodyRedacted);
        Assert.Null(notification.Body);
        Assert.Equal("delivered", notification.ProviderStatus);
    }
}
