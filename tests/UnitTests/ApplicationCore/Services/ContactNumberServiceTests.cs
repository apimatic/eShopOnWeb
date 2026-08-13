using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class ContactNumberServiceTests
{
    private readonly IRepository<ContactNumber> _contactNumbers = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly INotificationGateway _gateway = Substitute.For<INotificationGateway>();
    private readonly IAppLogger<ContactNumberService> _logger = Substitute.For<IAppLogger<ContactNumberService>>();

    private ContactNumberService CreateService() => new(_contactNumbers, _notifications, _gateway, _logger);

    [Fact]
    public async Task RegisterRejectsNumberProviderConsidersUnusable()
    {
        _gateway.ValidatePhoneNumberAsync("garbage", Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(false, null));

        var service = CreateService();

        await Assert.ThrowsAsync<PhoneNumberValidationException>(
            () => service.RegisterAsync("owner-1", "garbage"));

        await _contactNumbers.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterStoresProviderCanonicalNotRawInput()
    {
        _gateway.ValidatePhoneNumberAsync("(415) 555-0000", Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(true, "+14155550000"));
        _contactNumbers.FirstOrDefaultAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns((ContactNumber?)null);
        _contactNumbers.AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<ContactNumber>());

        var service = CreateService();
        var result = await service.RegisterAsync("owner-1", "(415) 555-0000");

        Assert.Equal("+14155550000", result.Number);
        await _contactNumbers.Received(1).AddAsync(
            Arg.Is<ContactNumber>(c => c.Number == "+14155550000" && c.OwnerId == "owner-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAnotherOwnersNumberIsReportedAsNotFound()
    {
        var otherOwners = new ContactNumber("someone-else", "+14155550000");
        _contactNumbers.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(otherOwners);

        var service = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync("owner-1", 99));
        await _contactNumbers.DidNotReceive().DeleteAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteCallsOffPendingMessagesToThatNumber()
    {
        var mine = new ContactNumber("owner-1", "+14155550000");
        _contactNumbers.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(mine);

        var pending = new OrderNotification(1, "owner-1", "+14155550000", NotificationType.DeliveryFollowUp, "hi");
        pending.RecordScheduled("SM123", NotificationStatus.Scheduled, System.DateTimeOffset.UtcNow.AddDays(3));
        _notifications.ListAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification> { pending });

        var service = CreateService();
        await service.DeleteAsync("owner-1", 5);

        await _gateway.Received(1).CancelScheduledAsync("SM123", Arg.Any<CancellationToken>());
        await _contactNumbers.Received(1).DeleteAsync(mine, Arg.Any<CancellationToken>());
        Assert.Equal(NotificationStatus.Canceled, pending.Status);
    }
}
