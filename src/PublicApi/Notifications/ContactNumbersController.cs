using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

[ApiController]
[Route("api/contact-numbers")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactNumbersController : ControllerBase
{
    private readonly NotificationWorkflowService _workflow;

    public ContactNumbersController(NotificationWorkflowService workflow)
    {
        _workflow = workflow;
    }

    [HttpPost]
    [ProducesResponseType(typeof(RegisterContactNumberResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<RegisterContactNumberResponse>> Register(
        RegisterContactNumberRequest request,
        CancellationToken cancellationToken)
    {
        var contact = await _workflow.RegisterContactNumberAsync(ShopperId(), request.PhoneNumber, cancellationToken);
        return Created(
            $"/api/contact-numbers/{contact.Id}",
            new RegisterContactNumberResponse(contact.Id, contact.CanonicalNumber));
    }

    [HttpGet]
    public async Task<ActionResult<ContactNumberResponse[]>> List(CancellationToken cancellationToken)
    {
        var contacts = await _workflow.GetContactNumbersAsync(ShopperId(), cancellationToken);
        return Ok(contacts.Select(x => new ContactNumberResponse(x.Id, x.CanonicalNumber, x.CreatedAt)));
    }

    [HttpDelete("{contactNumberId:int}")]
    public async Task<IActionResult> Delete(int contactNumberId, CancellationToken cancellationToken)
    {
        return await _workflow.DeleteContactNumberAsync(ShopperId(), contactNumberId, cancellationToken)
            ? NoContent()
            : NotFound();
    }

    private string ShopperId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new UnauthorizedAccessException();
}
