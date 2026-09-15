using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest
{
    [Required]
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. An unusable number is rejected here; what is
/// stored is the provider's own canonical form.
/// POST /api/contact-numbers
/// </summary>
public class RegisterContactNumberEndpoint
    : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
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
        if (string.IsNullOrWhiteSpace(request?.PhoneNumber))
        {
            return Results.BadRequest(new { message = "A phone number is required." });
        }

        var ownerId = _httpContextAccessor.RequireUserId();
        var result = await service.RegisterAsync(ownerId, request.PhoneNumber);

        if (!result.Accepted)
        {
            return Results.BadRequest(new { message = result.Rejection });
        }

        var number = result.ContactNumber!;
        var response = new RegisterContactNumberResponse
        {
            ContactNumberId = number.Id,
            PhoneNumber = number.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{number.Id}", response);
    }
}
