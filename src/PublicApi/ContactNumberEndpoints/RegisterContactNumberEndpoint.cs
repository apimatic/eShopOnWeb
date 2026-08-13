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

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a
/// usable destination is rejected here, and the provider's canonical form is what gets stored.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, IContactNumberService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, service, user);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service, ClaimsPrincipal user)
    {
        var ownerId = user.GetUserName();
        if (string.IsNullOrEmpty(ownerId))
            return Results.Unauthorized();

        var result = await service.RegisterAsync(ownerId, request.PhoneNumber ?? string.Empty);
        if (!result.Succeeded)
            return Results.BadRequest(new { error = result.Error });

        var number = result.ContactNumber!;
        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = number.Id,
            PhoneNumber = number.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{number.Id}", response);
    }
}

public class RegisterContactNumberRequest : BaseRequest
{
    public string? PhoneNumber { get; set; }
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(System.Guid correlationId) : base(correlationId) { }
    public RegisterContactNumberResponse() { }

    /// <summary>Identifier of the number just registered.</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form of the number that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
