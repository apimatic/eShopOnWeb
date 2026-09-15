using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.OrderNotifications;
using Microsoft.eShopWeb.PublicApi.Twilio;

namespace Microsoft.eShopWeb.PublicApi.ContactNumbers;

[ApiController]
[Route("api/contact-numbers")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactNumbersController : ControllerBase
{
    private readonly CatalogContext _db;
    private readonly ITwilioLookupClient _lookup;
    private readonly NotificationCoordinator _notifications;

    public ContactNumbersController(
        CatalogContext db,
        ITwilioLookupClient lookup,
        NotificationCoordinator notifications)
    {
        _db = db;
        _lookup = lookup;
        _notifications = notifications;
    }

    [HttpPost]
    public async Task<IActionResult> Register(RegisterContactNumberRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return BadRequest(new { error = "phoneNumber is required." });
        }

        TwilioPhoneLookup lookup;
        try
        {
            lookup = await _lookup.LookupAsync(request.PhoneNumber, cancellationToken);
        }
        catch (Exception)
        {
            return StatusCode(502, new { error = "The phone-number provider could not validate the destination." });
        }

        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.PhoneNumber))
        {
            return BadRequest(new { error = "The destination is not a valid phone number." });
        }

        var buyerId = User.Identity!.Name!;
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.BuyerId == buyerId && x.PhoneNumber == lookup.PhoneNumber,
            cancellationToken);
        if (contact == null)
        {
            contact = new ContactNumber(buyerId, lookup.PhoneNumber, DateTimeOffset.UtcNow);
            _db.ContactNumbers.Add(contact);
        }
        else
        {
            contact.Restore();
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Created($"/api/contact-numbers/{contact.Id}", new
        {
            contactNumberId = contact.Id,
            phoneNumber = contact.PhoneNumber
        });
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var contacts = await _db.ContactNumbers
            .Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderBy(x => x.Id)
            .Select(x => new { contactNumberId = x.Id, phoneNumber = x.PhoneNumber })
            .ToListAsync(cancellationToken);
        return Ok(new { contactNumbers = contacts });
    }

    [HttpDelete("{contactNumberId:int}")]
    public async Task<IActionResult> Delete(int contactNumberId, CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.BuyerId == buyerId,
            cancellationToken);
        if (contact == null)
        {
            return NotFound();
        }

        if (contact.DeletedAt == null)
        {
            contact.Remove(DateTimeOffset.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
        }

        await _notifications.CancelPendingFollowUpsForContactAsync(contact.Id, cancellationToken);
        return NoContent();
    }
}

public sealed class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}
