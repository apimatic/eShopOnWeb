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
/// destination is rejected here (400); the stored value is the provider's canonical form.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, IOrderNotificationService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var ownerId = user.ShopperId();
                if (string.IsNullOrEmpty(ownerId))
                    return Results.Unauthorized();

                request.OwnerId = ownerId;
                return await ExecuteAsync(request, service, ct);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(RegisterContactNumberRequest request, IOrderNotificationService service)
        => ExecuteAsync(request, service, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(RegisterContactNumberRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var contactNumber = await service.RegisterNumberAsync(request.OwnerId, request.PhoneNumber, cts.Token);
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", new RegisterContactNumberResponse
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        });
    }
}
