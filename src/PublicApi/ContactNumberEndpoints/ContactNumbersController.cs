using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Notifications;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

[ApiController]
[Route("api/contact-numbers")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactNumbersController : ControllerBase
{
    private readonly OrderNotificationService _service;

    public ContactNumbersController(OrderNotificationService service) => _service = service;

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterContactNumberRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var contact = await _service.RegisterContactNumberAsync(BuyerId(), request.PhoneNumber, cancellationToken);
            return Created($"/api/contact-numbers/{contact.Id}", new
            {
                contactNumberId = contact.Id,
                phoneNumber = contact.CanonicalNumber
            });
        }
        catch (WorkflowValidationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (WorkflowConflictException exception)
        {
            return Conflict(new { error = exception.Message });
        }
        catch (TwilioProviderException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Phone-number validation is temporarily unavailable." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var contacts = await _service.GetContactNumbersAsync(BuyerId(), cancellationToken);
        return Ok(new
        {
            contactNumbers = contacts.Select(x => new
            {
                contactNumberId = x.Id,
                phoneNumber = x.CanonicalNumber,
                createdAt = x.CreatedAt
            })
        });
    }

    [HttpDelete("{contactNumberId:int}")]
    public async Task<IActionResult> Delete(int contactNumberId, CancellationToken cancellationToken)
    {
        try
        {
            return await _service.DeleteContactNumberAsync(BuyerId(), contactNumberId, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (TwilioProviderException)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = "The destination was not removed because a queued message could not be cancelled." });
        }
    }

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new UnauthorizedAccessException("The token has no name claim.");
}

public sealed class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}
