using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
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
    private readonly ITwilioPhoneNumberValidator _validator;
    private readonly OrderNotificationService _notifications;

    public ContactNumbersController(CatalogContext db, ITwilioPhoneNumberValidator validator,
        OrderNotificationService notifications)
    {
        _db = db;
        _validator = validator;
        _notifications = notifications;
    }

    [HttpPost]
    public async Task<IActionResult> Register(RegisterContactNumberRequest request,
        CancellationToken cancellationToken)
    {
        PhoneNumberValidationResult validation;
        try
        {
            validation = await _validator.ValidateAsync(request.Number, cancellationToken);
        }
        catch (Exception ex) when (ex is TwilioProviderException or HttpRequestException or TaskCanceledException)
        {
            return Problem(statusCode: 502, title: "The phone number could not be validated by the provider.");
        }

        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
            return BadRequest(new { errors = new { number = new[] { "The provider does not consider this a valid destination." } } });

        var ownerId = User.Identity!.Name!;
        var existing = await _db.ContactNumbers.FirstOrDefaultAsync(
            x => x.OwnerId == ownerId && x.CanonicalNumber == validation.CanonicalNumber, cancellationToken);
        if (existing != null)
            return Ok(new { contactNumberId = existing.Id });

        var contact = new ContactNumber(ownerId, validation.CanonicalNumber);
        _db.ContactNumbers.Add(contact);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.Entry(contact).State = EntityState.Detached;
            existing = await _db.ContactNumbers.FirstOrDefaultAsync(
                x => x.OwnerId == ownerId && x.CanonicalNumber == validation.CanonicalNumber,
                cancellationToken);
            if (existing != null) return Ok(new { contactNumberId = existing.Id });
            throw;
        }
        return Created($"/api/contact-numbers/{contact.Id}", new { contactNumberId = contact.Id });
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var ownerId = User.Identity!.Name!;
        var numbers = await _db.ContactNumbers.Where(x => x.OwnerId == ownerId)
            .OrderBy(x => x.Id)
            .Select(x => new { contactNumberId = x.Id, number = x.CanonicalNumber, createdAt = x.CreatedAt })
            .ToListAsync(cancellationToken);
        return Ok(new { contactNumbers = numbers });
    }

    [HttpDelete("{contactNumberId:int}")]
    public async Task<IActionResult> Delete(int contactNumberId, CancellationToken cancellationToken)
    {
        var ownerId = User.Identity!.Name!;
        var contact = await _db.ContactNumbers.FirstOrDefaultAsync(
            x => x.Id == contactNumberId && x.OwnerId == ownerId, cancellationToken);
        if (contact == null) return NotFound();

        try
        {
            await _notifications.CancelOutstandingForContactAsync(contact.Id, cancellationToken);
        }
        catch (Exception ex) when (ex is TwilioProviderException or HttpRequestException or TaskCanceledException)
        {
            return Problem(statusCode: 502,
                title: "The provider could not confirm cancellation of pending messages; the number was not removed.");
        }

        _db.ContactNumbers.Remove(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed class RegisterContactNumberRequest
{
    [Required, MaxLength(64)] public string Number { get; set; } = string.Empty;
}
