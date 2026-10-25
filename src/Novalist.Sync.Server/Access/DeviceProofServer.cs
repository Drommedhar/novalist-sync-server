using Novalist.Sync.Contracts;

namespace Novalist.Sync.Server.Access;

public sealed class DeviceProofServer(IConfiguration configuration)
{
    public string? ServerUrl
    {
        get
        {
            var configured = configuration["Collaboration:ServerUrl"];
            if (string.IsNullOrWhiteSpace(configured)) return null;
            try { return DeviceProof.CanonicalServerUrl(configured); }
            catch (FormatException) { return null; }
        }
    }
}
