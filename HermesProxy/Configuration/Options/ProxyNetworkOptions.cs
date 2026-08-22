namespace HermesProxy.Configuration.Options;

public sealed class ProxyNetworkOptions
{
    public string ExternalAddress { get; set; } = "127.0.0.1";

    public int RestPort { get; set; } = 8081;

    public int BNetPort { get; set; } = 1119;

    public int RealmPort { get; set; } = 8084;

    public int InstancePort { get; set; } = 8086;

    /// <summary>
    /// Optional path to a custom PKCS#12 (.pfx) certificate served on the BNet TLS endpoints
    /// (BNetPort/RestPort). When unset, the embedded TrinityCore-compatible certificate is used.
    /// </summary>
    public string? CertificatePfxPath { get; set; }

    /// <summary>Password for <see cref="CertificatePfxPath"/>; null for a passwordless pfx.</summary>
    public string? CertificatePfxPassword { get; set; }

    /// <summary>
    /// Serve the REST login endpoint (<see cref="RestPort"/>) over plain HTTP instead of TLS,
    /// and advertise it to clients as an http:// URL.
    /// <para>
    /// Modern clients (the 5.5.0-engine generation) validate the login endpoint's certificate and
    /// abandon the connection during the handshake when it is the embedded self-signed one, which
    /// strands login with no HTTP request ever reaching the router. TrinityCore solves this the
    /// same way, choosing the scheme based on whether its dev wildcard certificate is in use.
    /// Only meaningful over a trusted network: the login form and its credentials travel in clear.
    /// </para>
    /// </summary>
    public bool RestPlaintext { get; set; }
}
