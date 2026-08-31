using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberRequest : BaseRequest
{
    public int ContactNumberId { get; set; }

    /// <summary>Populated from the JWT, never from the request.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class DeleteContactNumberResponse : BaseResponse
{
    public int ContactNumberId { get; set; }
    public bool Deleted { get; set; }
}

/// <summary>
/// Removes one of the signed-in shopper's registered numbers. Once removed,
/// nothing may be sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IRepository<ContactNumber>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest
                {
                    ContactNumberId = contactNumberId,
                    BuyerId = user.Identity!.Name!
                }, contactNumberRepository);
            })
            .Produces<DeleteContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IRepository<ContactNumber> contactNumberRepository)
    {
        var contactNumber = await contactNumberRepository.GetByIdAsync(request.ContactNumberId);

        // Not-found and not-owned are indistinguishable: a shopper must never
        // see another's numbers.
        if (contactNumber is null || contactNumber.BuyerId != request.BuyerId)
        {
            return Results.NotFound();
        }

        await contactNumberRepository.DeleteAsync(contactNumber);

        return Results.Ok(new DeleteContactNumberResponse
        {
            ContactNumberId = request.ContactNumberId,
            Deleted = true
        });
    }
}
