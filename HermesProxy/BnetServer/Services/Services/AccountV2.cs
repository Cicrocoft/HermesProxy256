// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Bgs.Protocol.Account.V2.Client;
using Framework.Constants;
using Framework.Logging;
using System.Collections.Generic;

namespace BNetServer.Services;

// Version 2 of the Battle.net account service. Modern clients call this right after logon to find
// out who they are; older clients get the same information through other paths and never touch it.
//
// The game account name returned by GetGameAccountInfo is load-bearing well beyond display: the
// client checks the realm join ticket against it, so a client that was told nothing here rejects
// the join with "Join token mismatch" and never opens the world connection.
//
// The two restriction methods (104, 203) are deliberately left to the dispatcher's empty-OK stub:
// this proxy has no ban data of its own — the legacy logon server already refused the login if the
// account was banned — so an empty restriction list is the truthful answer.
public partial class BnetServices
{
    [Service(ServiceRequirement.LoggedIn, OriginalHash.AccountServiceV2, 101)]
    BattlenetRpcErrorCode HandleGetAccountInfoV2(GetAccountInfoRequest request, GetAccountInfoResponse response)
    {
        response.Info = new AccountInfo
        {
            AccountId = GetSession().AccountInfo.Id,
        };

        return BattlenetRpcErrorCode.Ok;
    }

    [Service(ServiceRequirement.LoggedIn, OriginalHash.AccountServiceV2, 201)]
    BattlenetRpcErrorCode HandleGetGameAccountInfoV2(GetGameAccountInfoRequest request, GetGameAccountInfoResponse response)
    {
        if (request.GameAccount == null)
            return BattlenetRpcErrorCode.Ok;

        if (!GetSession().AccountInfo.GameAccounts.TryGetValue((uint)request.GameAccount.Id, out var gameAccount))
        {
            ServiceLog(LogType.Warn, $"Battlenet.GetGameAccountInfo(v2): unknown game account {request.GameAccount.Id}.");
            return BattlenetRpcErrorCode.Ok;
        }

        response.Info = new GameAccountInfo
        {
            AccountId = request.GameAccount.Id,
            Name = gameAccount.DisplayName,
        };

        return BattlenetRpcErrorCode.Ok;
    }
}
