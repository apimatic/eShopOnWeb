using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.ContactNumbersEndpoints;

[ApiController]
[Route("api/contact-numbers")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactNumbersController : ControllerBase
{
    private readonly CatalogContext _context;
    private readonly ISmsProvider _provider;
    private readonly OrderNotificationCoordinator _notifications;

    public ContactNumbersController(CatalogContext context, ISmsProvider provider, OrderNotificationCoordinator notifications)
    {
        _context = context;
        _provider = provider;
        _notifications = notifications;
    }

    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterContactNumberRequest request, CancellationToken cancellationToken)
    {
        PhoneNumberLookupResult lookup;
        try
        {
            lookup = await _provider.ValidatePhoneNumberAsync(request.PhoneNumber, cancellationToken);
        }
        catch (SmsProviderException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
            return ValidationProblem(new ValidationProblemDetails(new System.Collections.Generic.Dictionary<string, string[]>
            {
                [nameof(request.PhoneNumber)] = lookup.ValidationErrors.Count == 0
                    ? new[] { "Twilio does not consider this a valid destination." }
                    : lookup.ValidationErrors.ToArray()
            }));

        var buyerId = User.Identity!.Name!;
        var duplicate = await _context.ContactNumbers.AnyAsync(x => x.BuyerId == buyerId &&
            x.PhoneNumber == lookup.CanonicalPhoneNumber && x.RemovedAt == null, cancellationToken);
        if (duplicate) return Conflict(new { error = "That contact number is already registered." });

        var contact = new ContactNumber(buyerId, lookup.CanonicalPhoneNumber, DateTimeOffset.UtcNow);
        _context.ContactNumbers.Add(contact);
        await _context.SaveChangesAsync(cancellationToken);
        return Created($"/api/contact-numbers/{contact.Id}", new { contactNumberId = contact.Id });
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var contacts = await _context.ContactNumbers.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.RemovedAt == null)
            .OrderBy(x => x.Id)
            .Select(x => new { contactNumberId = x.Id, phoneNumber = x.PhoneNumber, createdAt = x.CreatedAt })
            .ToListAsync(cancellationToken);
        return Ok(new { contactNumbers = contacts });
    }

    [HttpDelete("{contactNumberId:int}")]
    public async Task<IActionResult> Delete(int contactNumberId, CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var contact = await _context.ContactNumbers.SingleOrDefaultAsync(x => x.Id == contactNumberId &&
            x.BuyerId == buyerId && x.RemovedAt == null, cancellationToken);
        if (contact is null) return NotFound();

        var orderIds = await _context.OrderNotifications
            .Where(x => x.ContactNumberId == contact.Id && x.Kind == NotificationKind.DeliveryFollowUp)
            .Select(x => x.OrderId).Distinct().ToListAsync(cancellationToken);
        foreach (var orderId in orderIds)
        {
            if (!await _notifications.CancelScheduledForOrderAsync(orderId, contact.Id, cancellationToken))
                return Problem("A provider-queued message could not be stopped; the contact number was not removed.",
                    statusCode: StatusCodes.Status502BadGateway);
        }

        contact.Remove(DateTimeOffset.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed class RegisterContactNumberRequest
{
    [Required, StringLength(64)]
    public string PhoneNumber { get; set; } = string.Empty;
}
