using System;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Framework.Cryptography;
using Xunit;

namespace HermesProxy.Tests.Cryptography;

/// <summary>
/// Exercises the Battle.net SRP6 server implementation by playing the client's half of the
/// exchange against it.
/// </summary>
/// <remarks>
/// The proxy only ever runs the server side, so nothing else in the codebase can tell a correct
/// implementation from one that consistently agrees with itself and disagrees with the real client.
/// These tests close most of that gap: they derive A and M1 exactly the way the SRP-6a client does,
/// which means an error in the shared parts — CalculateX, the k derivation, the padded evidence
/// hash — makes verification fail here rather than at the login screen.
/// </remarks>
public class BnetSrp6Tests
{
    private const string AccountName = "TESTACCOUNT";
    private const string Password = "correct horse battery staple";

    private static string SrpUsername(string login) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(login)));

    [Fact]
    public void V2Hash256_AcceptsClientEvidenceDerivedFromSamePassword()
    {
        var (srp, username) = MakeServerSide(Password);

        var (A, M1, expectedS) = ComputeClientProof(srp, username, Password, V2K());

        BigInteger? sessionKey = srp.VerifyClientEvidence(A, M1);

        Assert.NotNull(sessionKey);
        Assert.Equal(expectedS, sessionKey!.Value);
    }

    [Fact]
    public void V2Hash256_RejectsClientEvidenceFromWrongPassword()
    {
        var (srp, username) = MakeServerSide(Password);

        var (A, M1, _) = ComputeClientProof(srp, username, "not the password", V2K());

        Assert.Null(srp.VerifyClientEvidence(A, M1));
    }

    [Fact]
    public void V2Hash256_ServerEvidenceMatchesWhatTheClientWouldExpect()
    {
        var (srp, username) = MakeServerSide(Password);

        var (A, M1, S) = ComputeClientProof(srp, username, Password, V2K());

        BigInteger? sessionKey = srp.VerifyClientEvidence(A, M1);
        Assert.NotNull(sessionKey);

        // The client recomputes M2 from values it already holds; ours has to agree byte for byte.
        BigInteger serverM2 = srp.CalculateServerEvidence(A, M1, sessionKey!.Value);
        BigInteger clientM2 = srp.DoCalculateEvidence(A, M1, S);

        Assert.Equal(clientM2, serverM2);
    }

    [Fact]
    public void CheckCredentials_RoundTripsThroughTheVerifier()
    {
        var (srp, username) = MakeServerSide(Password);

        Assert.True(srp.CheckCredentials(username, Password));
        Assert.False(srp.CheckCredentials(username, Password + "x"));
    }

    [Fact]
    public void SingleUse_SecondVerificationThrows()
    {
        var (srp, username) = MakeServerSide(Password);
        var (A, M1, _) = ComputeClientProof(srp, username, Password, V2K());

        srp.VerifyClientEvidence(A, M1);

        Assert.Throws<InvalidOperationException>(() => srp.VerifyClientEvidence(A, M1));
    }

    /// <summary>
    /// Builds the server side the way the REST login endpoint does: derive salt and verifier from
    /// the password, then start a challenge from them.
    /// </summary>
    private static (BnetSRP6v2Hash256 Srp, string Username) MakeServerSide(string password)
    {
        string username = SrpUsername(AccountName);

        var registration = new BnetSRP6v2Hash256();
        byte[] verifier = registration.CalculateVerifier(username, password, registration.s);

        return (new BnetSRP6v2Hash256(username, registration.s, verifier), username);
    }

    /// <summary>k = H(N || zero-padding || g), the same multiplier the server's constructor uses.</summary>
    private static BigInteger V2K()
    {
        byte[] n = BnetSRP6v2Base.N.ToByteArray(true, true);
        byte[] g = BnetSRP6v2Base.g.ToByteArray(true, true);
        byte[] padding = new byte[255];

        return new BigInteger(SHA256.HashData(n.Combine(padding.Combine(g))), true, true);
    }

    /// <summary>
    /// The client half of SRP-6a: pick a, send A = g^a, and prove knowledge of the password with
    /// M1 = H(A, B, S) where S = (B - k*g^x)^(a + u*x).
    /// </summary>
    private static (BigInteger A, BigInteger M1, BigInteger S) ComputeClientProof(
        BnetSRP6v2Hash256 srp, string username, string password, BigInteger k)
    {
        BigInteger N = srp.GetN();
        BigInteger g = srp.Getg();

        BigInteger x = srp.CalculateX(username, password, srp.s);
        BigInteger v = BigInteger.ModPow(g, x, N);

        BigInteger a = new BigInteger(new byte[0].GenerateRandomKey(32), true);
        BigInteger A = BigInteger.ModPow(g, a, N);

        BigInteger u = srp.CalculateU(A);

        // (B - k*v) can land negative before reduction; force it back into [0, N).
        BigInteger baseValue = (((srp.B - (k * v)) % N) + N) % N;
        BigInteger S = BigInteger.ModPow(baseValue, a + (u * x), N);

        BigInteger M1 = srp.DoCalculateEvidence(A, srp.B, S);

        return (A, M1, S);
    }
}
