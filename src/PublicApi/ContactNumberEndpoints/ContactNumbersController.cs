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
using Microsoft.eShopWeb.PublicApi.Notifications;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

[ApiController]
[Route("api/contact-numbers")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ContactNumbersController : ControllerBase
{
    private readonly CatalogContext _context;
    private readonly ITwilioMessagingClient _twilio;
    private readonly NotificationCoordinator _notifications;
    private readonly TimeProvider _clock;

    public ContactNumbersController(
        CatalogContext context,
        ITwilioMessagingClient twilio,
        NotificationCoordinator notifications,
        TimeProvider clock)
    {
        _context = context;
        _twilio = twilio;
        _notifications = notifications;
        _clock = clock;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(RegisterContactNumberRequest request, CancellationToken cancellationToken)
    {
        ValidatedPhoneNumber validated;
        try
        {
            validated = await _twilio.ValidatePhoneNumberAsync(request.PhoneNumber, request.CountryCode, cancellationToken);
        }
        catch (TwilioProviderException)
        {
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Phone-number validation is temporarily unavailable.");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Phone-number validation is temporarily unavailable.");
        }

        if (!validated.IsValid || string.IsNullOrWhiteSpace(validated.CanonicalNumber))
        {
            return BadRequest(new ValidationProblemDetails(new System.Collections.Generic.Dictionary<string, string[]>
            {
                [nameof(request.PhoneNumber)] = validated.ValidationErrors.Count == 0
                    ? new[] { "The messaging provider does not consider this a valid destination." }
                    : validated.ValidationErrors.Select(x => $"Provider validation: {x}.").ToArray()
            }));
        }

        var ownerId = User.Identity!.Name!;
        if (await _context.ContactNumbers.AnyAsync(
                x => x.OwnerId == ownerId && x.CanonicalNumber == validated.CanonicalNumber,
                cancellationToken))
        {
            return Conflict(new { message = "That contact number is already registered." });
        }

        var contactNumber = new ContactNumber(ownerId, validated.CanonicalNumber, _clock.GetUtcNow());
        _context.ContactNumbers.Add(contactNumber);
        await _context.SaveChangesAsync(cancellationToken);

        return Created($"/api/contact-numbers/{contactNumber.Id}", new
        {
            contactNumberId = contactNumber.Id,
            phoneNumber = contactNumber.CanonicalNumber
        });
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var ownerId = User.Identity!.Name!;
        var numbers = await _context.ContactNumbers
            .Where(x => x.OwnerId == ownerId)
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                contactNumberId = x.Id,
                phoneNumber = x.CanonicalNumber,
                createdAt = x.CreatedAt
            })
            .ToListAsync(cancellationToken);
        return Ok(numbers);
    }

    [HttpDelete("{contactNumberId:int}")]
    public async Task<IActionResult> Delete(int contactNumberId, CancellationToken cancellationToken)
    {
        var ownerId = User.Identity!.Name!;
        var contactNumber = await _context.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.OwnerId == ownerId,
            cancellationToken);
        if (contactNumber == null)
        {
            return NotFound();
        }

        if (!await _notifications.CancelPendingMessagesForContactAsync(contactNumber.Id, cancellationToken))
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The number could not be removed safely because a provider-queued message could not be cancelled.");
        }

        _context.ContactNumbers.Remove(contactNumber);
        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed class RegisterContactNumberRequest
{
    [Required, StringLength(64, MinimumLength = 3)]
    public string PhoneNumber { get; init; } = string.Empty;

    [RegularExpression("^[A-Za-z]{2}$")]
    public string? CountryCode { get; init; }
}
