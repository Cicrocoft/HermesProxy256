// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using System.Runtime.Serialization;

namespace Framework.Web;

/// <summary>
/// Reply to POST /bnetserver/login/srp/ — the parameters a client needs to run its half of the SRP
/// exchange. All big numbers are uppercase hex, big-endian.
/// </summary>
/// <remarks>
/// The JSON names are fixed by the client. Note <c>public_B</c> capitalises the B while every other
/// name is lowercase; that asymmetry is real, not a typo.
/// </remarks>
[DataContract]
public class SrpLoginChallenge
{
    [DataMember(Name = "version")]
    public int Version { get; set; }

    [DataMember(Name = "iterations")]
    public int Iterations { get; set; }

    [DataMember(Name = "modulus")]
    public string? Modulus { get; set; }

    [DataMember(Name = "generator")]
    public string? Generator { get; set; }

    [DataMember(Name = "hash_function")]
    public string? HashFunction { get; set; }

    [DataMember(Name = "username")]
    public string? Username { get; set; }

    [DataMember(Name = "salt")]
    public string? Salt { get; set; }

    [DataMember(Name = "public_B")]
    public string? PublicB { get; set; }

    [DataMember(Name = "eligible_credential_upgrade")]
    public bool EligibleCredentialUpgrade { get; set; }
}
