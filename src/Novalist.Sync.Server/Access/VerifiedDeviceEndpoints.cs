using System.Security.Claims;
using Novalist.Sync.Contracts;
using Novalist.Sync.Server.Hubs;

namespace Novalist.Sync.Server.Access;

public static class VerifiedDeviceEndpoints
{
    public static IEndpointRouteBuilder MapVerifiedDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/devices/registration", async (ClaimsPrincipal principal, UserDirectory users, DeviceProofServer proofs, CancellationToken ct) =>
            new DeviceRegistrationContext((await users.GetOrCreateAsync(principal, ct)).Id, proofs.ServerUrl)).RequireAuthorization();
        app.MapPut("/devices/verified", RegisterAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> RegisterAsync(VerifiedDeviceRegistration request, ClaimsPrincipal principal,
        UserDirectory users, DeviceRegistry devices, ConnectionRegistry connections, DeviceProofServer proofs, CancellationToken ct)
    {
        try
        {
            var user = await users.GetOrCreateAsync(principal, ct);
            var change = await devices.RegisterVerifiedAsync(user.Id, request, proofs.ServerUrl, ct);
            if (change == DeviceRegistrationChange.FirstVerified) connections.AbortUser(user.Id);
            else if (change == DeviceRegistrationChange.Linked) connections.AbortDevice(request.Keys.DeviceId);
            return Results.NoContent();
        }
        catch (FormatException error) { return Results.Problem(detail: error.Message, statusCode: StatusCodes.Status400BadRequest); }
        catch (ConflictException error) { return Results.Problem(detail: error.Message, statusCode: StatusCodes.Status409Conflict); }
        catch (AccessDeniedException error) { return Results.Problem(detail: error.Message, statusCode: StatusCodes.Status403Forbidden); }
    }
}
