using System;
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

public class RegisterContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public RegisterContactNumberResponse() { }

    /// <summary>Top-level identifier of the created contact number.</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical form of the number that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated and canonicalized
/// by the provider; a number that is not a usable destination is rejected here (400).
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, ClaimsPrincipal>
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
            (RegisterContactNumberRequest request, ClaimsPrincipal user) =>
                await HandleAsync(request, user))
            .Produces<RegisterContactNumberResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        using var cts = EndpointBudget.Start();
        var result = await _contactNumberService.RegisterAsync(buyerId, request.PhoneNumber, cts.Token);
        if (!result.Succeeded || result.ContactNumber is null)
            return Results.BadRequest(new { error = result.Error });

        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = result.ContactNumber.Id,
            PhoneNumber = result.ContactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
    }
}
