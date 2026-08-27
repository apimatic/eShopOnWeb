using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's contact numbers. Another shopper's number is
/// indistinguishable from a nonexistent one.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, ClaimsPrincipal>
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;

    public DeleteContactNumberEndpoint(IRepository<ContactNumber> contactNumberRepository)
    {
        _contactNumberRepository = contactNumberRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId), user);
            })
            .Produces<DeleteContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, ClaimsPrincipal user)
    {
        var ownerId = user.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        var contactNumber = await _contactNumberRepository.GetByIdAsync(request.ContactNumberId);
        if (contactNumber is null || contactNumber.OwnerId != ownerId)
        {
            return Results.NotFound();
        }

        await _contactNumberRepository.DeleteAsync(contactNumber);

        return Results.Ok(new DeleteContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = request.ContactNumberId
        });
    }
}

public class DeleteContactNumberRequest : BaseRequest
{
    public DeleteContactNumberRequest(int contactNumberId)
    {
        ContactNumberId = contactNumberId;
    }

    public int ContactNumberId { get; }
}

public class DeleteContactNumberResponse : BaseResponse
{
    public DeleteContactNumberResponse(Guid correlationId) : base(correlationId) {}
    public DeleteContactNumberResponse() {}

    public int ContactNumberId { get; set; }
}
