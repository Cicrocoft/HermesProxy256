// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using System.Runtime.Serialization;

namespace Framework.Web;

/// <summary>
/// Join token handed to a client in Param_RealmJoinTicket, and presented again on the world
/// connection to identify the game account.
/// </summary>
/// <remarks>
/// Older clients accept the bare game account name here. Modern ones expect this JSON object and
/// reject anything else with "Join token mismatch" at the glue screen, without ever opening the
/// world connection. The three numeric fields are FourCC codes of the client's build variant —
/// "Win", "x64", "WoW" and friends — the same values the client reports as its platform and title.
/// </remarks>
[DataContract]
public class RealmJoinTicket
{
    [DataMember(Name = "gameAccount")]
    public string? GameAccount { get; set; }

    [DataMember(Name = "platform")]
    public int Platform { get; set; }

    [DataMember(Name = "type")]
    public int Type { get; set; }

    [DataMember(Name = "clientArch")]
    public int ClientArch { get; set; }
}
