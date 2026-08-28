using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

[ApiController]
[Route("api/contact-numbers")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactNumbersController : ControllerBase
{
    private readonly IOrderNotificationService _service;
    public ContactNumbersController(IOrderNotificationService service) => _service = service;

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterContactNumberRequest request, CancellationToken ct)
    {
        var result = await _service.RegisterContactNumberAsync(BuyerId(), request.Number, request.CountryCode, ct);
        return Created($"/api/contact-numbers/{result.ContactNumberId}", new
        {
            contactNumberId = result.ContactNumberId,
            number = result.Number,
            createdAt = result.CreatedAt
        });
    }

    [HttpGet]
    public Task<IReadOnlyList<ContactNumberView>> List(CancellationToken ct) =>
        _service.GetContactNumbersAsync(BuyerId(), ct);

    [HttpDelete("{contactNumberId:int}")]
    public async Task<IActionResult> Delete(int contactNumberId, CancellationToken ct) =>
        await _service.DeleteContactNumberAsync(BuyerId(), contactNumberId, ct) ? NoContent() : NotFound();

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new UnauthorizedAccessException("The token has no shopper identity.");
}

public sealed record RegisterContactNumberRequest(string Number, string? CountryCode);
