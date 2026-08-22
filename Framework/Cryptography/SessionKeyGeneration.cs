/*
 * Copyright (C) 2012-2020 CypherCore <http://github.com/CypherCore>
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Security.Cryptography;

namespace Framework.Cryptography;

public sealed class SessionKeyGenerator
{
    private readonly int HashSize;
    private readonly HashAlgorithmName _algorithm;

    private readonly byte[] _o0;
    private readonly byte[] _o1;
    private readonly byte[] _o2;
    private int _taken;

    /// <param name="sha512">
    /// The 5.5.0-generation engine builds the session key with SHA-512 instead of SHA-256. The
    /// construction is otherwise identical, so only the hash and its output size change.
    /// </param>
    public SessionKeyGenerator(ReadOnlySpan<byte> buff, bool sha512 = false)
    {
        HashSize = sha512 ? 64 : 32;
        _algorithm = sha512 ? HashAlgorithmName.SHA512 : HashAlgorithmName.SHA256;
        _o0 = new byte[HashSize];
        _o1 = new byte[HashSize];
        _o2 = new byte[HashSize];

        int halfSize = buff.Length / 2;
        if (sha512)
        {
            SHA512.HashData(buff[..halfSize], _o1);
            SHA512.HashData(buff[halfSize..], _o2);
        }
        else
        {
            SHA256.HashData(buff[..halfSize], _o1);
            SHA256.HashData(buff[halfSize..], _o2);
        }
        FillUp();
    }

    public SessionKeyGenerator(byte[] buff, int size, bool sha512 = false)
        : this(buff.AsSpan(0, size), sha512) { }

    public void Generate(Span<byte> buf)
    {
        for (int i = 0; i < buf.Length; i++)
        {
            if (_taken == HashSize)
                FillUp();

            buf[i] = _o0[_taken];
            _taken++;
        }
    }

    public void Generate(byte[] buf, uint sz) => Generate(buf.AsSpan(0, (int)sz));

    private void FillUp()
    {
        using var ih = IncrementalHash.CreateHash(_algorithm);
        ih.AppendData(_o1);
        ih.AppendData(_o0);
        ih.AppendData(_o2);

        // Hash directly into _o0 to avoid the byte[] allocation that GetHashAndReset would produce.
        if (!ih.TryGetHashAndReset(_o0, out int written) || written != HashSize)
            throw new CryptographicException($"SessionKeyGenerator.FillUp: {_algorithm} produced unexpected output size.");

        _taken = 0;
    }
}
