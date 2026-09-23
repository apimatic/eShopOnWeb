using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    /// <summary>Set from the caller's token in the route handler; never bound from the request body.</summary>
    public string? OwnerId { get; set; }
}

public record RegisterContactNumberResponse(int ContactNumberId, string PhoneNumber);

/// <summary>
/// Registers a mobile number for the signed-in shopper. The provider must consider it a usable destination
/// (else 400); the provider's canonical E.164 form is what gets stored.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, IContactNumberService service, ClaimsPrincipal user) =>
            {
                request.OwnerId = user.FindFirstValue(ClaimTypes.Name);
                return await HandleAsync(request, service);
            })
            .Produces<RegisterContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
    {
        if (string.IsNullOrEmpty(request.OwnerId)) return Results.Unauthorized();

        var contactNumber = await service.RegisterAsync(request.OwnerId, request.PhoneNumber, default);
        return Results.Ok(new RegisterContactNumberResponse(contactNumber.Id, contactNumber.PhoneNumber));
    }
}
