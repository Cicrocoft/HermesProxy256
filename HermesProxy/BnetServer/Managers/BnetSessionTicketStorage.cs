// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Serialization;
using Framework.Web;
using HermesProxy;
using System;
using System.Collections.Generic;

namespace BNetServer;

public static class BnetSessionTicketStorage
{
    public static Dictionary<string, GlobalSessionData> SessionsByName = new();
    public static Dictionary<string, GlobalSessionData> SessionsByTicket = new();
    public static Dictionary<ulong, GlobalSessionData> SessionsByKey = new();

    public static void AddNewSessionByName(string name, GlobalSessionData session)
    {
        if (SessionsByName.ContainsKey(name))
        {
            SessionsByName[name].OnDisconnect();
            SessionsByName[name] = session;
        }
        else
            SessionsByName.Add(name, session);
    }

    public static void AddNewSessionByTicket(string loginTicket, GlobalSessionData session)
    {
        if (SessionsByTicket.ContainsKey(loginTicket))
        {
            SessionsByTicket[loginTicket].OnDisconnect();
            SessionsByTicket[loginTicket] = session;
        }
        else
            SessionsByTicket.Add(loginTicket, session);
    }

    /// <summary>
    /// Finds the session a world connection belongs to, given the join ticket the client presented.
    /// </summary>
    /// <remarks>
    /// Sessions are stored under the game account name, which is what clients up to 3.4.3 present
    /// here verbatim. The modern generation is handed a JSON ticket instead (see
    /// RealmManager.BuildJoinTicket) and echoes that whole object back, so the account name has to
    /// be read out of it before the lookup can succeed.
    ///
    /// The comparison is case-insensitive either way: the name is stored as the user typed it at
    /// the login form, while the ticket carries the account name as the auth server normalised it.
    /// </remarks>
    public static bool TryGetSessionByJoinTicket(string joinTicket, out GlobalSessionData? session)
    {
        string accountName = joinTicket;

        if (joinTicket.StartsWith('{'))
        {
            RealmJoinTicket? parsed = null;
            try
            {
                parsed = Json.CreateObject<RealmJoinTicket>(joinTicket);
            }
            catch (Exception)
            {
                // Not a ticket we issued; fall through and try the raw string.
            }

            if (!string.IsNullOrEmpty(parsed?.GameAccount))
                accountName = parsed!.GameAccount!;
        }

        if (SessionsByName.TryGetValue(accountName, out session))
            return true;

        foreach (var pair in SessionsByName)
        {
            if (string.Equals(pair.Key, accountName, StringComparison.OrdinalIgnoreCase))
            {
                session = pair.Value;
                return true;
            }
        }

        session = null;
        return false;
    }

    public static void AddNewSessionByKey(ulong connectKey, GlobalSessionData session)
    {
        if (SessionsByKey.ContainsKey(connectKey))
        {
            SessionsByKey[connectKey].OnDisconnect();
            SessionsByKey[connectKey] = session;
        }
        else
            SessionsByKey.Add(connectKey, session);
    }
}
