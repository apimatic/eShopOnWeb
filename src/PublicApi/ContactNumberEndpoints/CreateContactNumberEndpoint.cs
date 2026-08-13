using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a usable
/// destination is rejected here; what gets stored is the provider's own canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, ClaimsPrincipal user, IContactNumberService service) =>
            {
                request.Caller = CallerIdentity.GetUserName(user);
                return await HandleAsync(request, service);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService service)
    {
        if (string.IsNullOrEmpty(request.Caller))
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return Results.BadRequest(new { error = "A phone number is required." });

        var result = await service.RegisterAsync(request.Caller, request.PhoneNumber);
        if (!result.Success)
            return Results.BadRequest(new { error = result.Error, validationErrors = result.ValidationErrors });

        var contactNumber = result.ContactNumber!;
        var response = new CreateContactNumberResponse
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber,
            RegisteredAt = contactNumber.RegisteredAt
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}

public class CreateContactNumberRequest
{
    /// <summary>The mobile number to register, in any form the provider can canonicalise.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Set from the JWT, not the request body — any value sent in the body is ignored.</summary>
    [JsonIgnore]
    public string? Caller { get; set; }
}

public class CreateContactNumberResponse
{
    /// <summary>The identifier of the number that was registered.</summary>
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public System.DateTimeOffset RegisteredAt { get; set; }
}
