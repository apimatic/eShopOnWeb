using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the caller's contact numbers. Afterwards nothing may be sent to it again.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class DeleteContactNumberEndpoint : EndpointBaseAsync
    .WithRequest<int>
    .WithActionResult
{
    private readonly IContactNumberService _contactNumberService;

    public DeleteContactNumberEndpoint(IContactNumberService contactNumberService)
    {
        _contactNumberService = contactNumberService;
    }

    [HttpDelete("api/contact-numbers/{contactNumberId}")]
    [SwaggerOperation(
        Summary = "Deletes one of the caller's contact numbers",
        Description = "Deletes one of the caller's contact numbers",
        OperationId = "contact-numbers.delete",
        Tags = new[] { "ContactNumberEndpoints" })
    ]
    public override async Task<ActionResult> HandleAsync(int contactNumberId,
        CancellationToken cancellationToken = default)
    {
        var buyerId = User.Identity!.Name!;
        await _contactNumberService.DeleteAsync(buyerId, contactNumberId, cancellationToken);
        return NoContent();
    }
}
