using System.Collections.Generic;
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

public class RegisterContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse : BaseResponse
{
    public int ContactNumberId { get; set; }
    public ContactNumberDto? ContactNumber { get; set; }
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. An unusable destination is rejected here,
/// and the provider's canonical form of the number is what gets stored.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint
{
    private readonly IContactNumberService _contactNumberService;

    public RegisterContactNumberEndpoint(IContactNumberService contactNumberService)
    {
        _contactNumberService = contactNumberService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, CancellationToken ct) =>
                await HandleAsync(request, user, ct))
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var ownerId = user.GetUsername();
        if (string.IsNullOrEmpty(ownerId))
            return Results.Unauthorized();

        var contactNumber = await _contactNumberService.RegisterAsync(ownerId, request.PhoneNumber, ct);

        var response = new RegisterContactNumberResponse
        {
            ContactNumberId = contactNumber.Id,
            ContactNumber = new ContactNumberDto
            {
                ContactNumberId = contactNumber.Id,
                PhoneNumber = contactNumber.PhoneNumber,
                RegisteredAt = contactNumber.RegisteredAt
            }
        };

        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
