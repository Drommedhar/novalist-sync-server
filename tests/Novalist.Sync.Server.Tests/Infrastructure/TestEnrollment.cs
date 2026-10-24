using Microsoft.IdentityModel.JsonWebTokens;
using Novalist.Sync.Contracts;
using NSec.Cryptography;

namespace Novalist.Sync.Server.Tests.Infrastructure;

internal static class TestEnrollment
{
    public static void Authorize(HttpClient http, byte[] signingPublicKey)
    {
        var token = http.DefaultRequestHeaders.Authorization?.Parameter ?? throw new InvalidOperationException("A test account token is required.");
        var subject = new JsonWebTokenHandler().ReadJsonWebToken(token).Subject;
        http.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.Enrollment(subject, signingPublicKey));
    }

    public static VerifiedDeviceRegistration First(Guid user, DevicePublicKeys keys, Key signing)
    {
        using var recoverySigning = Key.Create(SignatureAlgorithm.Ed25519);
        using var recoveryReceiving = Key.Create(KeyAgreementAlgorithm.X25519);
        var recovery = new DevicePublicKeys(Guid.NewGuid(), recoverySigning.PublicKey.Export(KeyBlobFormat.RawPublicKey),
            recoveryReceiving.PublicKey.Export(KeyBlobFormat.RawPublicKey));
        return new(keys, SignatureAlgorithm.Ed25519.Sign(signing, DeviceProof.EncodeRegistration(SyncServer.PublicUrl, user, keys)),
            RecoveryDevice: Recovery(user, keys.DeviceId, signing, recovery, recoverySigning));
    }

    public static DeviceRecoveryRegistration Recovery(Guid user, Guid rootDevice, Key rootSigning, DevicePublicKeys recovery, Key recoverySigning) =>
        new(recovery, SignatureAlgorithm.Ed25519.Sign(recoverySigning, DeviceProof.EncodeRegistration(SyncServer.PublicUrl, user, recovery)),
            SignatureAlgorithm.Ed25519.Sign(rootSigning, DeviceProof.EncodeLink(SyncServer.PublicUrl, user, rootDevice, recovery)));
}
