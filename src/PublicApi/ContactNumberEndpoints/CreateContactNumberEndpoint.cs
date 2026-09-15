using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a usable
/// destination is rejected here; the provider's canonical form is what gets stored.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, IContactNumberService service, CancellationToken ct) =>
            {
                return await HandleAsync(request, user, service, ct);
            })
            .Produces<RegisterContactNumberResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, ClaimsPrincipal user,
        IContactNumberService service, CancellationToken ct)
    {
        var ownerId = user.GetUserId();
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var contactNumber = await service.RegisterAsync(ownerId, request.PhoneNumber, ct);

        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}

public class RegisterContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId) { }

    public RegisterContactNumberResponse() { }

    /// <summary>The identifier of the number just registered.</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical (E.164) form that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
