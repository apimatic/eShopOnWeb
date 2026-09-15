using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Services;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

[ApiController]
[Route("api/contact-numbers")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactNumbersController : ControllerBase
{
    private readonly CatalogContext _db;
    private readonly ITwilioMessagingClient _twilio;
    private readonly OrderNotificationService _notifications;
    private readonly TimeProvider _clock;

    public ContactNumbersController(CatalogContext db, ITwilioMessagingClient twilio,
        OrderNotificationService notifications, TimeProvider clock)
    {
        _db = db;
        _twilio = twilio;
        _notifications = notifications;
        _clock = clock;
    }

    [HttpPost]
    public async Task<IActionResult> Register(RegisterContactNumberRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber) || request.PhoneNumber.Length > 64)
        {
            return BadRequest(new { error = "A phone number is required." });
        }

        ValidatedPhoneNumber validated;
        try
        {
            validated = await _twilio.ValidatePhoneNumberAsync(request.PhoneNumber, cancellationToken);
        }
        catch (TwilioProviderException exception)
        {
            return StatusCode(exception.StatusCode is >= 400 and < 500
                ? (int)HttpStatusCode.BadGateway
                : exception.StatusCode, new { error = "Phone number validation is currently unavailable." });
        }

        if (!validated.IsValid || string.IsNullOrWhiteSpace(validated.CanonicalNumber))
        {
            return BadRequest(new { error = "The messaging provider does not consider this a valid destination." });
        }

        var buyerId = User.Identity!.Name!;
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.BuyerId == buyerId && x.CanonicalNumber == validated.CanonicalNumber, cancellationToken);
        if (contact is null)
        {
            contact = new ContactNumber(buyerId, validated.CanonicalNumber, _clock.GetUtcNow());
            _db.ContactNumbers.Add(contact);
        }
        else if (!contact.IsActive)
        {
            contact.Reactivate(_clock.GetUtcNow());
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Created($"/api/contact-numbers/{contact.Id}", new
        {
            contactNumberId = contact.Id,
            phoneNumber = contact.CanonicalNumber
        });
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var contacts = await _db.ContactNumbers.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.IsActive)
            .OrderBy(x => x.Id)
            .Select(x => new { contactNumberId = x.Id, phoneNumber = x.CanonicalNumber })
            .ToListAsync(cancellationToken);
        return Ok(new { contactNumbers = contacts });
    }

    [HttpDelete("{contactNumberId:int}")]
    public async Task<IActionResult> Delete(int contactNumberId, CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.BuyerId == buyerId && x.IsActive, cancellationToken);
        if (contact is null)
        {
            return NotFound();
        }

        try
        {
            await _notifications.DeactivateContactAsync(contact, cancellationToken);
        }
        catch (TwilioProviderException)
        {
            return StatusCode((int)HttpStatusCode.BadGateway,
                new { error = "Pending provider messages could not be cancelled; the number remains registered." });
        }

        return NoContent();
    }
}

public sealed class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}
