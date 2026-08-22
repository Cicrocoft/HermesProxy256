// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

// Ported from CypherCore (Source/Framework/Cryptography/SRP6.cs), the project this proxy was
// forked from. Only the Battle.net variants are carried over — the legacy Grunt SRP6 used against
// the emulator's own logon server lives in the auth client and is unrelated to this.
//
// This is the server side of the SRP exchange that modern clients perform against the REST login
// endpoint: they never transmit the password, only a public ephemeral A and an evidence value M1
// proving they know it. Verifying that requires the account's salt and verifier, which this proxy
// derives from a configured password (see AccountOptions) since it has no account database.
//
// The byte-order and padding conventions below are load-bearing and match the client exactly;
// GetBrokenEvidenceVector in particular reproduces a Blizzard quirk where the evidence hash is fed
// a value one byte longer than the number strictly needs. Do not "clean up" any of it.

using System;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Framework.Cryptography;

public abstract class SRP6
{
    public static int SaltLength = 32;

    /// <summary>s - the user's password salt, random, used to calculate v on registration.</summary>
    public byte[] s = new byte[SaltLength];

    /// <summary>H(I) - the username, all uppercase.</summary>
    protected BigInteger I;

    /// <summary>b - randomly chosen by the server, same length as N, never given out.</summary>
    protected BigInteger b;

    /// <summary>v - the user's password verifier, derived from s + H(USERNAME || ':' || PASSWORD).</summary>
    protected BigInteger v;

    /// <summary>B = k*v + g^b</summary>
    public BigInteger B;

    // A single instance can only be used to verify once.
    bool _used;

    protected SRP6()
    {
        s = new byte[0].GenerateRandomKey(SaltLength);
        _used = true;
    }

    protected SRP6(BigInteger i, byte[] salt, byte[] verifier, BigInteger N, BigInteger g, BigInteger k)
    {
        s = salt;
        I = i;
        b = CalculatePrivateB(N);
        v = new BigInteger(verifier, true);
        B = CalculatePublicB(N, g, k);
    }

    public BigInteger? VerifyClientEvidence(BigInteger A, BigInteger clientM1)
    {
        if (_used)
            throw new InvalidOperationException("A single SRP6 object must only ever be used to verify ONCE!");
        _used = true;

        return DoVerifyClientEvidence(A, clientM1);
    }

    public bool CheckCredentials(string username, string password)
    {
        return v == new BigInteger(CalculateVerifier(username, password, s), true);
    }

    static BigInteger CalculatePrivateB(BigInteger N)
    {
        BigInteger b = new BigInteger(new byte[0].GenerateRandomKey((int)N.GetBitLength()), true);
        b %= N - 1;
        return b;
    }

    BigInteger CalculatePublicB(BigInteger N, BigInteger g, BigInteger k)
    {
        return (BigInteger.ModPow(g, b, N) + (v * k)) % N;
    }

    public byte[] CalculateVerifier(string username, string password, byte[] salt)
    {
        // v = g ^ H(s || H(u || ':' || p)) mod N
        return BigInteger.ModPow(Getg(), CalculateX(username, password, salt), GetN()).ToByteArray();
    }

    public abstract BigInteger GetN();
    public abstract BigInteger Getg();

    public abstract BigInteger CalculateServerEvidence(BigInteger A, BigInteger clientM1, BigInteger K);

    public abstract BigInteger CalculateX(string username, string password, byte[] salt);

    public abstract BigInteger? DoVerifyClientEvidence(BigInteger A, BigInteger clientM1);
}

public abstract class BnetSRP6Base : SRP6
{
    protected BnetSRP6Base() : base() { }

    protected BnetSRP6Base(BigInteger i, byte[] salt, byte[] verifier, BigInteger N, BigInteger g, BigInteger k)
        : base(i, salt, verifier, N, g, k) { }

    public override BigInteger CalculateServerEvidence(BigInteger A, BigInteger clientM1, BigInteger K)
    {
        s_evidenceWidth = _evidenceWidth;
        try
        {
            return DoCalculateEvidence(A, clientM1, K);
        }
        finally
        {
            s_evidenceWidth = 0;
        }
    }

    public override BigInteger? DoVerifyClientEvidence(BigInteger A, BigInteger clientM1)
    {
        BigInteger N = GetN();
        if ((A % N).IsZero)
            return null;

        // Two independent encoding conventions are in play and neither is pinned down, so search
        // both rather than assume. Each is a place where a BigInteger has to be turned back into a
        // fixed-width byte string, and .NET drops leading zero bytes where the client keeps them:
        //
        //   * u = H(A || B) - a value one bit short of N gives a 255-byte string where the client
        //     hashes 256, changing u, hence S, hence M1;
        //   * the evidence vector - see GetBrokenEvidenceVector.
        //
        // Both misfire only when a random value happens to land under a byte boundary, which is why
        // this presented as intermittent login failures rather than as a broken login. Four cheap
        // combinations, and the one that verifies is recorded so M2 is encoded the same way.
        int fixedWidth = (int)(N.GetBitLength() + 7) >> 3;
        BigInteger lastS = BigInteger.Zero;

        foreach (bool padU in new[] { false, true })
        {
            s_padUInputs = padU;
            BigInteger u;
            try
            {
                u = CalculateU(A);
            }
            finally
            {
                s_padUInputs = false;
            }
            if ((u % N).IsZero)
                continue;

            BigInteger S = BigInteger.ModPow(A * BigInteger.ModPow(v, u, N), b, N);
            lastS = S;

            foreach (int width in new[] { 0, fixedWidth })
            {
                s_evidenceWidth = width;
                BigInteger ourM;
                try
                {
                    ourM = DoCalculateEvidence(A, B, S);
                }
                finally
                {
                    s_evidenceWidth = 0;
                }
                if (ourM != clientM1)
                    continue;

                _evidenceWidth = width;
                Framework.Logging.Log.Print(Framework.Logging.LogType.Debug,
                    $"SRP evidence matched: u={(padU ? "padded" : "trimmed")}, " +
                    $"evidence={(width == 0 ? "bit-length-derived" : $"{width}-byte fixed")}.");
                return S;
            }
        }

        // No encoding of ours reproduces the client's M1. That rules the padding conventions out and
        // leaves the shared secret itself - i.e. the password the client was given does not match
        // the configured one. Say so, because "incorrect password" is otherwise indistinguishable
        // from the padding faults above and we have chased the wrong one before.
        Framework.Logging.Log.Print(Framework.Logging.LogType.Warn,
            $"SRP: no padding convention reproduces the client evidence (A={A.GetBitLength()} bits, " +
            $"B={B.GetBitLength()}, S={lastS.GetBitLength()}, N={N.GetBitLength()}) - " +
            "the client's password does not match the configured one.");
        return null;
    }

    /// <summary>The encoding that verified this exchange; M2 has to use the same one.</summary>
    int _evidenceWidth;

    /// <summary>
    /// When non-zero, every hashed value is left-padded to this many bytes instead of to a width
    /// derived from its own bit length. Thread-static because the hashing helpers are static and
    /// one exchange must not disturb another's encoding.
    /// </summary>
    [ThreadStatic]
    protected static int s_evidenceWidth;

    /// <summary>
    /// When set, the operands of u = H(A || B) are left-padded to the width of N instead of being
    /// emitted at their natural length. Same thread-static reasoning as s_evidenceWidth.
    /// </summary>
    [ThreadStatic]
    protected static bool s_padUInputs;

    /// <summary>The A || B byte string that u is computed over, under whichever convention is being tried.</summary>
    protected byte[] UInputs(BigInteger A)
    {
        if (!s_padUInputs)
            return A.ToByteArray(true, true).Combine(B.ToByteArray(true, true));

        int width = (int)(GetN().GetBitLength() + 7) >> 3;
        return LeftPad(A, width).Combine(LeftPad(B, width));
    }

    static byte[] LeftPad(BigInteger value, int width)
    {
        var bytes = value.ToByteArray(true, true);
        return bytes.Length >= width ? bytes : new byte[width - bytes.Length].Combine(bytes);
    }

    // With s_evidenceWidth unset this left-pads to (bitLength + 8) / 8 bytes, one byte longer than
    // the value needs whenever the bit length is a multiple of 8 - so the encoded width is 127, 128
    // or 129 bytes for a 1024-bit N depending on the random values of that exchange. The client
    // hashes a single fixed width, which is why logins were rejected until the numbers happened to
    // line up. DoVerifyClientEvidence tries both.
    static byte[] GetBrokenEvidenceVector(BigInteger bn)
    {
        var byteArray = bn.ToByteArray(true, true);
        int bytes = s_evidenceWidth > 0 ? s_evidenceWidth : (int)(bn.GetBitLength() + 8) >> 3;
        if (bytes <= byteArray.Length)
            return byteArray;
        return new byte[bytes - byteArray.Length].Combine(byteArray);
    }

    public abstract byte GetVersion();
    public abstract uint GetXIterations();

    public abstract BigInteger CalculateU(BigInteger A);

    public abstract BigInteger DoCalculateEvidence(params BigInteger[] bns);

    protected static BigInteger DoCalculateEvidenceHash256(params BigInteger[] bns)
    {
        using var sha256 = SHA256.Create();
        return HashEvidence(sha256, bns);
    }

    protected static BigInteger DoCalculateEvidenceHash512(params BigInteger[] bns)
    {
        using var sha512 = SHA512.Create();
        return HashEvidence(sha512, bns);
    }

    static BigInteger HashEvidence(HashAlgorithm hash, BigInteger[] bns)
    {
        for (var i = 0; i < bns.Length; ++i)
        {
            var bytes = GetBrokenEvidenceVector(bns[i]);
            if (i == bns.Length - 1)
            {
                hash.TransformFinalBlock(bytes, 0, bytes.Length);
                break;
            }

            hash.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }

        return new BigInteger(hash.Hash!, true, true);
    }
}

public abstract class BnetSRP6v1Base : BnetSRP6Base
{
    /// <summary>The modulus, an algorithm parameter; all operations are mod this.</summary>
    public static readonly BigInteger N;

    /// <summary>A generator for the ring of integers mod N, algorithm parameter.</summary>
    public static readonly BigInteger g;

    // g is a single byte but is hashed left-padded to the width of N when deriving k.
    protected static readonly byte[] dummyBytes = new byte[127];

    static BnetSRP6v1Base()
    {
        g = new BigInteger(2u);
        N = new BigInteger("86A7F6DEEB306CE519770FE37D556F29944132554DED0BD68205E27F3231FEF5A10108238A3150C59CAF7B0B6478691C13A6ACF5E1B5ADAFD4A943D4A21A142B800E8A55F8BFBAC700EB77A7235EE5A609E350EA9FC19F10D921C2FA832E4461B7125D38D254A0BE873DFC27858ACB3F8B9F258461E4373BC3A6C2A9634324AB".ToByteArray(), true, true);
    }

    protected BnetSRP6v1Base() : base() { }

    protected BnetSRP6v1Base(string username, byte[] salt, byte[] verifier, BigInteger k)
        : base(new BigInteger(SHA256.HashData(Encoding.UTF8.GetBytes(username)), true), salt, verifier, N, g, k) { }

    public override BigInteger CalculateX(string username, string password, byte[] salt)
    {
        return new BigInteger(SHA256.HashData(salt.Combine(SHA256.HashData(Encoding.UTF8.GetBytes(username + ":" + password)))), true);
    }

    public override BigInteger GetN() { return N; }
    public override BigInteger Getg() { return g; }

    public override byte GetVersion() { return 1; }
    public override uint GetXIterations() { return 1; }
}

public abstract class BnetSRP6v2Base : BnetSRP6Base
{
    /// <summary>The modulus, an algorithm parameter; all operations are mod this.</summary>
    public static readonly BigInteger N;

    /// <summary>A generator for the ring of integers mod N, algorithm parameter.</summary>
    public static readonly BigInteger g;

    protected static readonly byte[] dummyBytes = new byte[255];

    static BnetSRP6v2Base()
    {
        N = new BigInteger("AC6BDB41324A9A9BF166DE5E1389582FAF72B6651987EE07FC3192943DB56050A37329CBB4A099ED8193E0757767A13DD52312AB4B03310DCD7F48A9DA04FD50E8083969EDB767B0CF6095179A163AB3661A05FBD5FAAAE82918A9962F0B93B855F97993EC975EEAA80D740ADBF4FF747359D041D5C33EA71D281E446B14773BCA97B43A23FB801676BD207A436C6481F1D2B9078717461A5B9D32E688F87748544523B524B0D57D5EA77A2775D2ECFA032CFBDBF52FB3786160279004E57AE6AF874E7303CE53299CCC041C7BC308D82A5698F3A8D0C38271AE35F8E9DBFBB694B5C803D89F7AE435DE236D525F54759B65E372FCD68EF20FA7111F9E4AFF73".ToByteArray(), true, true);
        g = new BigInteger(2u);
    }

    protected BnetSRP6v2Base() : base() { }

    protected BnetSRP6v2Base(string username, byte[] salt, byte[] verifier, BigInteger k)
        : base(new BigInteger(SHA256.HashData(Encoding.UTF8.GetBytes(username)), true), salt, verifier, N, g, k) { }

    public override BigInteger CalculateX(string username, string password, byte[] salt)
    {
        string tmp = username + ":" + password;

        using Rfc2898DeriveBytes rfc = new(tmp, salt, (int)GetXIterations(), HashAlgorithmName.SHA512);
        byte[] xBytes = rfc.GetBytes(SHA512.HashSizeInBytes);
        BigInteger x = new(xBytes, true, true);
        if ((xBytes[0] & 0x80) != 0)
        {
            byte[] fix = [1, .. new byte[64]];
            x -= new BigInteger(fix, true);
        }

        if (x.Sign == -1)
            return x += N - 1;

        return x % (N - 1);
    }

    /// <summary>
    /// True when PBKDF2's output has its top bit set - the one place where our reading of x can
    /// differ from the client's. <see cref="CalculateX"/> then reinterprets the 64 bytes as a signed
    /// 512-bit integer and subtracts 2^512; a client reading them unsigned derives a different x,
    /// and therefore a different verifier, session key and evidence.
    ///
    /// The salt is the server's to choose and is regenerated on every challenge, so which reading
    /// applies is decided by a coin flip per login attempt. That is the observed behaviour: the same
    /// password rejected and then accepted seconds later, with nothing else changed - 41 rejections
    /// against 15 matches on 22 Aug, and the verification log showing no padding convention
    /// reproducing the client's evidence on the failures.
    ///
    /// Which side is right is not settled here, and does not need to be: pick a salt where the two
    /// readings cannot disagree. Costs one extra PBKDF2 pass on average.
    /// </summary>
    public static bool XReadingIsAmbiguous(string username, string password, byte[] salt, uint iterations)
    {
        using Rfc2898DeriveBytes rfc = new(username + ":" + password, salt, (int)iterations, HashAlgorithmName.SHA512);
        return (rfc.GetBytes(SHA512.HashSizeInBytes)[0] & 0x80) != 0;
    }

    public override BigInteger GetN() { return N; }
    public override BigInteger Getg() { return g; }

    public override byte GetVersion() { return 2; }
    public override uint GetXIterations() { return 15000; }
}

public class BnetSRP6v1Hash256 : BnetSRP6v1Base
{
    public BnetSRP6v1Hash256() : base() { }

    public BnetSRP6v1Hash256(string username, byte[] salt, byte[] verifier)
        : base(username, salt, verifier, new BigInteger(SHA256.HashData(N.ToByteArray(true, true).Combine(dummyBytes.Combine(g.ToByteArray(true, true)))), true, true)) { }

    public override BigInteger CalculateU(BigInteger A)
    {
        return new BigInteger(SHA256.HashData(UInputs(A)), true, true);
    }

    public override BigInteger DoCalculateEvidence(params BigInteger[] bns) => DoCalculateEvidenceHash256(bns);
}

public class BnetSRP6v1Hash512 : BnetSRP6v1Base
{
    public BnetSRP6v1Hash512() : base() { }

    public BnetSRP6v1Hash512(string username, byte[] salt, byte[] verifier)
        : base(username, salt, verifier, new BigInteger(SHA512.HashData(N.ToByteArray(true, true).Combine(dummyBytes.Combine(g.ToByteArray(true, true)))), true, true)) { }

    public override BigInteger CalculateU(BigInteger A)
    {
        return new BigInteger(SHA512.HashData(UInputs(A)), true, true);
    }

    public override BigInteger DoCalculateEvidence(params BigInteger[] bns) => DoCalculateEvidenceHash512(bns);
}

public class BnetSRP6v2Hash256 : BnetSRP6v2Base
{
    public BnetSRP6v2Hash256() : base() { }

    public BnetSRP6v2Hash256(string username, byte[] salt, byte[] verifier)
        : base(username, salt, verifier, new BigInteger(SHA256.HashData(N.ToByteArray(true, true).Combine(dummyBytes.Combine(g.ToByteArray(true, true)))), true, true)) { }

    public override BigInteger CalculateU(BigInteger A)
    {
        return new BigInteger(SHA256.HashData(UInputs(A)), true, true);
    }

    public override BigInteger DoCalculateEvidence(params BigInteger[] bns) => DoCalculateEvidenceHash256(bns);
}

public class BnetSRP6v2Hash512 : BnetSRP6v2Base
{
    public BnetSRP6v2Hash512() : base() { }

    // Note: unlike the other three, upstream derives k here without the g left-padding. Preserved
    // verbatim — a client using this variant expects exactly what CypherCore produces.
    public BnetSRP6v2Hash512(string username, byte[] salt, byte[] verifier)
        : base(username, salt, verifier, new BigInteger(SHA512.HashData(N.ToByteArray(true, true).Combine(g.ToByteArray(true, true))), true, true)) { }

    public override BigInteger CalculateU(BigInteger A)
    {
        return new BigInteger(SHA512.HashData(UInputs(A)), true, true);
    }

    public override BigInteger DoCalculateEvidence(params BigInteger[] bns) => DoCalculateEvidenceHash512(bns);
}
