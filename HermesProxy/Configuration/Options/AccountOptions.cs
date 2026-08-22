namespace HermesProxy.Configuration.Options;

/// <summary>
/// Credentials for the account on the legacy emulator.
/// <para>
/// Only needed by clients that log in through SRP, which is how the modern (5.5.0-engine)
/// generation authenticates against the REST login endpoint. Those clients never transmit the
/// password — they prove knowledge of it — so the proxy cannot learn it from the login form the
/// way older clients let it. It still needs the password in the clear to complete its own SRP6
/// handshake with the emulator's logon server, and the two exchanges use different parameters and
/// verifiers, so one cannot be relayed into the other.
/// </para>
/// <para>
/// Leave empty for older clients: they post the password directly and this is ignored.
/// </para>
/// </summary>
public sealed class AccountOptions
{
    /// <summary>
    /// Account name on the legacy emulator. Only used to check that the name typed at the login
    /// screen is the account this proxy is configured for; the typed name is what gets forwarded.
    /// </summary>
    public string Username { get; set; } = "";

    /// <summary>
    /// Password for <see cref="Username"/>. Stored in the clear, so treat the config file
    /// accordingly. A wrong value fails the client's SRP proof, which surfaces as a normal
    /// "invalid password" at the login screen.
    /// </summary>
    public string Password { get; set; } = "";

    /// <summary>
    /// SRP version offered in the login challenge: 1 (single SHA-256 pass) or 2 (15000 rounds of
    /// PBKDF2-SHA512). 2 matches what current CypherCore issues for new accounts. Configurable so
    /// a client that rejects one can be pointed at the other without a rebuild.
    /// </summary>
    public int SrpVersion { get; set; } = 2;
}
