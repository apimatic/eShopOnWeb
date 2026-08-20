using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ITwilioSmsClient _twilio;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ITwilioSmsClient twilio,
        IAppLogger<ContactNumberService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _twilio = twilio;
        _logger = logger;
    }

    public async Task<Result<ShopperContactNumber>> RegisterAsync(string buyerId, string phoneNumber)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(phoneNumber, nameof(phoneNumber));

        PhoneNumberLookupResult lookup;
        try
        {
            lookup = await _twilio.LookupAsync(phoneNumber);
        }
        catch (TwilioClientException ex)
        {
            _logger.LogWarning("Phone number lookup failed with HTTP {Status} code {Code}.", ex.HttpStatus, ex.ErrorCode ?? 0);
            return Result<ShopperContactNumber>.Invalid(new List<ValidationError>
            {
                new() { Identifier = nameof(phoneNumber), ErrorMessage = "The number could not be validated as a usable destination." }
            });
        }

        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalE164))
        {
            var reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "not a usable destination";
            return Result<ShopperContactNumber>.Invalid(new List<ValidationError>
            {
                new() { Identifier = nameof(phoneNumber), ErrorMessage = $"The provider does not consider this a usable destination ({reason})." }
            });
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndE164Specification(buyerId, lookup.CanonicalE164));
        if (existing is not null)
        {
            return Result<ShopperContactNumber>.Success(existing);
        }

        var created = new ShopperContactNumber(buyerId, lookup.CanonicalE164, lookup.NationalFormat);
        await _contactNumbers.AddAsync(created);
        return Result<ShopperContactNumber>.Success(created);
    }

    public async Task<IReadOnlyList<ShopperContactNumber>> ListForBuyerAsync(string buyerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var list = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
        return list;
    }

    public async Task<Result> DeleteAsync(string buyerId, int contactNumberId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var contact = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpecification(contactNumberId, buyerId));
        if (contact is null)
        {
            return Result.NotFound();
        }

        var destination = contact.E164Number;
        var scheduled = await _notifications.ListAsync(
            new ScheduledNotificationsByDestinationSpecification(buyerId, destination));
        foreach (var notification in scheduled.Where(n => n.IsScheduledPending() && n.ProviderMessageSid is not null))
        {
            try
            {
                var updated = await _twilio.UpdateAsync(notification.ProviderMessageSid!, body: null, status: "canceled");
                notification.ApplyProviderState(updated.Status, updated.ErrorCode, bodyIfNotRedacted: null);
                await _notifications.UpdateAsync(notification);
            }
            catch (TwilioClientException ex)
            {
                _logger.LogWarning(
                    "Failed to cancel scheduled notification {NotificationId} (provider {Sid}) with HTTP {Status} code {Code}.",
                    notification.Id, notification.ProviderMessageSid ?? string.Empty, ex.HttpStatus, ex.ErrorCode ?? 0);
            }
        }

        await _contactNumbers.DeleteAsync(contact);
        return Result.Success();
    }
}
