using System.Linq;
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
    private readonly OrderNotificationService _service;

    public ContactNumbersController(OrderNotificationService service) => _service = service;

    [HttpPost]
    public async Task<ActionResult<RegisterContactNumberResponse>> Register(
        RegisterContactNumberRequest request,
        CancellationToken ct)
    {
        try
        {
            var contact = await _service.RegisterContactAsync(User.Identity!.Name!, request.Number, ct);
            return Created($"/api/contact-numbers/{contact.Id}", new RegisterContactNumberResponse(contact.Id));
        }
        catch (InvalidContactNumberException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message, Status = StatusCodes.Status400BadRequest });
        }
        catch (TwilioProviderException ex)
        {
            return ProviderProblem(ex);
        }
    }

    [HttpGet]
    public async Task<ActionResult> Get(CancellationToken ct)
    {
        var contacts = await _service.GetContactsAsync(User.Identity!.Name!, ct);
        return Ok(contacts.Select(x => new ContactNumberResponse(x.Id, x.CanonicalNumber, x.RegisteredAt)));
    }

    [HttpDelete("{contactNumberId:int}")]
    public async Task<IActionResult> Delete(int contactNumberId, CancellationToken ct) =>
        await _service.DeleteContactAsync(User.Identity!.Name!, contactNumberId, ct) ? NoContent() : NotFound();

    private ObjectResult ProviderProblem(TwilioProviderException ex)
    {
        int? providerStatus = ex.StatusCode is null ? null : (int)ex.StatusCode;
        var status = providerStatus switch
        {
            401 or 403 => StatusCodes.Status502BadGateway,
            429 => StatusCodes.Status503ServiceUnavailable,
            >= 400 and < 500 => providerStatus.Value,
            _ => StatusCodes.Status502BadGateway
        };
        return StatusCode(status, new ProblemDetails { Title = ex.Message, Status = status });
    }
}
