using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
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
using Microsoft.eShopWeb.Infrastructure.Services;

namespace Microsoft.eShopWeb.PublicApi.ContactNumbers;

[ApiController]
[Route("api/contact-numbers")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactNumbersController : ControllerBase
{
    private readonly CatalogContext _db;
    private readonly IMessagingProvider _provider;
    private readonly OrderNotificationDispatcher _notifications;

    public ContactNumbersController(CatalogContext db, IMessagingProvider provider,
        OrderNotificationDispatcher notifications)
    {
        _db = db;
        _provider = provider;
        _notifications = notifications;
    }

    [HttpPost]
    [ProducesResponseType(typeof(RegisterContactNumberResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<RegisterContactNumberResponse>> Register(
        RegisterContactNumberRequest request, CancellationToken cancellationToken)
    {
        PhoneNumberValidation validation;
        try
        {
            validation = await _provider.ValidatePhoneNumberAsync(request.PhoneNumber, cancellationToken);
        }
        catch (MessagingProviderException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }

        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            return ValidationProblem(new ValidationProblemDetails(new System.Collections.Generic.Dictionary<string, string[]>
            {
                [nameof(request.PhoneNumber)] = validation.ValidationErrors.Any()
                    ? validation.ValidationErrors.ToArray()
                    : new[] { "Twilio does not consider this a valid destination." }
            }));
        }

        var ownerId = UserId();
        var duplicate = await _db.ContactNumbers.AnyAsync(x => x.OwnerId == ownerId &&
            x.CanonicalNumber == validation.CanonicalNumber && x.RemovedAt == null, cancellationToken);
        if (duplicate)
        {
            return Conflict(new ProblemDetails { Detail = "That contact number is already registered." });
        }

        var contact = new ContactNumber(ownerId, validation.CanonicalNumber, DateTimeOffset.UtcNow);
        _db.ContactNumbers.Add(contact);
        await _db.SaveChangesAsync(cancellationToken);

        return Created($"/api/contact-numbers/{contact.Id}",
            new RegisterContactNumberResponse(contact.Id, contact.CanonicalNumber));
    }

    [HttpGet]
    public async Task<ActionResult<ContactNumberResponse[]>> Get(CancellationToken cancellationToken)
    {
        var ownerId = UserId();
        var numbers = await _db.ContactNumbers
            .Where(x => x.OwnerId == ownerId && x.RemovedAt == null)
            .OrderBy(x => x.Id)
            .Select(x => new ContactNumberResponse(x.Id, x.CanonicalNumber, x.CreatedAt))
            .ToArrayAsync(cancellationToken);
        return Ok(numbers);
    }

    [HttpDelete("{contactNumberId:int}")]
    public async Task<IActionResult> Delete(int contactNumberId, CancellationToken cancellationToken)
    {
        var ownerId = UserId();
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.OwnerId == ownerId, cancellationToken);
        if (contact is null)
        {
            return NotFound();
        }

        contact.Remove(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            await _notifications.CancelScheduledForContactAsync(contact.Id, CancellationToken.None);
        }
        catch
        {
            // The contact remains removed even if provider reconciliation must be retried by repeating this request.
        }

        return NoContent();
    }

    private string UserId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new InvalidOperationException("The authenticated token has no name claim.");
}

public sealed class RegisterContactNumberRequest
{
    [Required, StringLength(64, MinimumLength = 3)]
    public string PhoneNumber { get; set; } = string.Empty;
}

public sealed record RegisterContactNumberResponse(int ContactNumberId, string PhoneNumber);
public sealed record ContactNumberResponse(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedAt);
