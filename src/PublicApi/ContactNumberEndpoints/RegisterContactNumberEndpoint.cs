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
/// Registers a mobile number for the signed-in shopper. The provider validates it and its
/// canonical form is what gets stored; a number the provider does not consider usable is rejected.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RegisterContactNumberEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, IContactNumberService service) =>
                await HandleAsync(request, service))
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
    {
        var ownerId = _httpContextAccessor.HttpContext!.User.Identity!.Name!;
        var result = await service.RegisterAsync(ownerId, request.PhoneNumber ?? string.Empty);

        if (result.Outcome == ActionOutcome.BadRequest)
        {
            return Results.BadRequest(new { error = result.Error });
        }

        var response = new RegisterContactNumberResponse
        {
            ContactNumberId = result.ContactNumberId,
            PhoneNumber = result.PhoneNumber!
        };
        return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
    }
}

public class RegisterContactNumberRequest
{
    /// <summary>The mobile number to register, in any format the provider can normalise.</summary>
    public string? PhoneNumber { get; set; }
}

public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}
