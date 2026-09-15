using System;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.ContactNumbers;

[ApiController]
[Route("api/contact-numbers")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactNumbersController : ControllerBase
{
    private readonly CatalogContext _db;
    private readonly ITwilioGateway _twilio;

    public ContactNumbersController(CatalogContext db, ITwilioGateway twilio)
    {
        _db = db;
        _twilio = twilio;
    }

    [HttpPost]
    public async Task<IActionResult> Register(RegisterContactNumberRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return BadRequest(new { error = "phoneNumber is required." });

        PhoneNumberValidation validation;
        try
        {
            validation = await _twilio.ValidateMobileNumberAsync(request.PhoneNumber, cancellationToken);
        }
        catch (Exception ex) when (ex is TwilioProviderException or HttpRequestException or TaskCanceledException)
        {
            return Problem(statusCode: 502, title: "The phone-number provider could not validate the destination.");
        }

        if (!validation.IsUsableMobile || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
            return BadRequest(new { error = validation.Reason });

        var buyerId = BuyerId();
        var existing = await _db.ContactNumbers.SingleOrDefaultAsync(x =>
            x.BuyerId == buyerId && x.CanonicalNumber == validation.CanonicalNumber, cancellationToken);
        if (existing is not null)
            return Ok(new { contactNumberId = existing.Id, phoneNumber = existing.CanonicalNumber });

        var contact = new ContactNumber(buyerId, validation.CanonicalNumber, DateTimeOffset.UtcNow);
        _db.ContactNumbers.Add(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return Created($"/api/contact-numbers/{contact.Id}",
            new { contactNumberId = contact.Id, phoneNumber = contact.CanonicalNumber });
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var buyerId = BuyerId();
        var contacts = await _db.ContactNumbers.AsNoTracking().Where(x => x.BuyerId == buyerId)
            .OrderBy(x => x.Id).Select(x => new
            {
                contactNumberId = x.Id,
                phoneNumber = x.CanonicalNumber,
                createdAt = x.CreatedAt
            }).ToListAsync(cancellationToken);
        return Ok(contacts);
    }

    [HttpDelete("{contactNumberId:int}")]
    public async Task<IActionResult> Delete(int contactNumberId, CancellationToken cancellationToken)
    {
        var buyerId = BuyerId();
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(x =>
            x.Id == contactNumberId && x.BuyerId == buyerId, cancellationToken);
        if (contact is null) return NotFound();

        var scheduled = await _db.OrderNotifications.Where(x => x.ContactNumberId == contact.Id &&
            x.ScheduledFor != null && x.ProviderDateSent == null && x.ProviderMessageSid != null &&
            x.ProviderStatus != "canceled").ToListAsync(cancellationToken);
        foreach (var notification in scheduled)
        {
            try
            {
                var result = await _twilio.CancelMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode,
                    result.ErrorMessage, result.DateSent);
            }
            catch (Exception)
            {
                return Problem(statusCode: 502,
                    title: "The number was not removed because Twilio did not confirm cancellation of pending messages.");
            }
            if (!string.Equals(notification.ProviderStatus, "canceled", StringComparison.OrdinalIgnoreCase))
                return Problem(statusCode: 502,
                    title: "The number was not removed because Twilio did not cancel a pending message.");
        }

        _db.ContactNumbers.Remove(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new InvalidOperationException("The authenticated token has no name claim.");
}

public sealed class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}
