// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Bgs.Protocol;
using Bgs.Protocol.Authentication.V2.Client;
using Framework.Constants;
using Framework.Logging;
using Framework.Realm;
using Google.Protobuf;
using System;
using BNetServer.Networking;

namespace BNetServer.Services;

// Version 2 of the Battle.net authentication service.
//
// Clients running the modern (5.5.0-era) engine — TBC Classic Anniversary 2.5.6.69110 among
// them — call AuthenticationService v2 rather than v1. The flow mirrors v1: validate the
// logon request, then hand the client a web-auth URL through the listener, after which the
// existing REST login path takes over. The differences that matter:
//
//   * v2 LogonRequest identifies the game with a numeric title_id instead of v1's
//     Program string, and carries optional LogonOptions (device id, cached auth token).
//   * the external challenge is delivered as ExternalChallengeNotification on
//     AuthenticationListener v2 method 4, not as ChallengeExternalRequest on ChallengeListener.
//
// The challenge payload deliberately keeps v1's platform/version/locale path segments rather
// than TrinityCore's bare "/bnetserver/login/", because this proxy's REST service routes on
// those segments. The client treats the payload as an opaque URL either way.
public partial class BnetServices
{
    [Service(ServiceRequirement.Unauthorized, OriginalHash.AuthenticationServiceV2, 1)]
    BattlenetRpcErrorCode HandleLogonV2(LogonRequest logonRequest, NoData response)
    {
        if (logonRequest.ApplicationVersion != HermesProxy.ModernVersion.BuildInt)
        {
            ServiceLog(LogType.Error, $"Battlenet.LogonRequest(v2): Attempted to log in with wrong game version (using {logonRequest.ApplicationVersion}, expected {HermesProxy.ModernVersion.BuildInt})!");
            return BattlenetRpcErrorCode.BadVersion;
        }

        if (logonRequest.Platform != "Win" && logonRequest.Platform != "Wn64" && logonRequest.Platform != "Mc64" && logonRequest.Platform != "MacA")
        {
            ServiceLog(LogType.Error, $"Battlenet.LogonRequest(v2): Attempted to log in from an unsupported platform (using {logonRequest.Platform})!");
            return BattlenetRpcErrorCode.BadPlatform;
        }

        if (!LocaleChecker.IsValidLocale(logonRequest.Locale.ToEnum<Locale>()))
        {
            ServiceLog(LogType.Error, $"Battlenet.LogonRequest(v2): Attempted to log in with unsupported locale (using {logonRequest.Locale})!");
            return BattlenetRpcErrorCode.BadLocale;
        }

        // title_id replaces v1's Program string. The per-title values aren't documented here, so
        // log it rather than reject on it — a wrong title would surface later as a bad realm list.
        ServiceLog(LogType.Debug, $"Battlenet.LogonRequest(v2): titleId={logonRequest.TitleId}, platform={logonRequest.Platform}, locale={logonRequest.Locale}, version={logonRequest.ApplicationVersion}");

        var endpoint = LoginServiceManager.Instance.GetAddressForClient(GetRemoteIpEndPoint().Address);

        ExternalChallengeNotification externalChallenge = new();
        externalChallenge.PayloadType = "web_auth_url";
        externalChallenge.Payload = ByteString.CopyFromUtf8($"{LoginServiceManager.Instance.LoginUrlScheme}://{endpoint.Address}:{endpoint.Port}/bnetserver/login/{logonRequest.Platform}/{logonRequest.ApplicationVersion}/{logonRequest.Locale}/");

        SendRequest(OriginalHash.AuthenticationListenerV2, 4, externalChallenge);
        return BattlenetRpcErrorCode.Ok;
    }

    // FourCC of "WoW", the title this proxy serves. CypherCore derives the same value from the
    // string; spelled out here because HermesProxy has no FourCC helper.
    private const uint WowTitleId = ('W' << 16) | ('o' << 8) | 'W';

    /// <summary>
    /// Closes out a v2 login. The client presents the ticket it got from the REST endpoint, and the
    /// reply carries the session key the world connection will be encrypted with.
    /// </summary>
    /// <remarks>
    /// Same job as v1's VerifyWebCredentials, but the result travels as a LogonCompleteNotification
    /// on AuthenticationListener v2 method 1 rather than a LogonResult on the v1 listener, and the
    /// account id is a plain integer instead of v1's EntityId with its hand-picked high bits.
    /// </remarks>
    [Service(ServiceRequirement.Unauthorized, OriginalHash.AuthenticationServiceV2, 2)]
    BattlenetRpcErrorCode HandleVerifyAuthTokenV2(VerifyAuthTokenRequest request, NoData response)
    {
        if (request.AuthToken == null)
            return BattlenetRpcErrorCode.Denied;

        string authToken = request.AuthToken.ToStringUtf8();
        if (!BnetSessionTicketStorage.SessionsByTicket.TryGetValue(authToken, out var tmpSession))
        {
            ServiceLog(LogType.Error, $"Battlenet.VerifyAuthToken(v2): no session for ticket '{authToken}'.");
            return BattlenetRpcErrorCode.Denied;
        }

        tmpSession.AccountInfo = new AccountInfo(tmpSession.Username);

        if (tmpSession.AccountInfo.LoginTicketExpiry < Time.UnixTime)
            return BattlenetRpcErrorCode.TimedOut;

        if (tmpSession.AccountInfo.IsBanned)
        {
            if (tmpSession.AccountInfo.IsPermanenetlyBanned)
            {
                ServiceLog(LogType.Debug, $"Battlenet.VerifyAuthToken(v2): banned account {tmpSession.AccountInfo.Login} tried to login!");
                return BattlenetRpcErrorCode.GameAccountBanned;
            }

            ServiceLog(LogType.Debug, $"Battlenet.VerifyAuthToken(v2): temporarily banned account {tmpSession.AccountInfo.Login} tried to login!");
            return BattlenetRpcErrorCode.GameAccountSuspended;
        }

        tmpSession.SessionKey = new byte[64].GenerateRandomKey(64);

        // FIXME(256-spike): temporary diagnostics. Remove before any PR. This prints key material,
        // which normally must never reach a log — it is here only to feed an offline search for
        // this build's auth key against a local dev account, and the run is throwaway.
        ServiceLog(LogType.Warn,
            $"[256-spike] CAPTURE bnetSessionKey={System.Convert.ToHexString(tmpSession.SessionKey)}");

        // FIXME(256-spike): dump the random bnet session key we hand the client, to compare against
        // the Param_BnetSessionKey it later echoes on join. Remove with the rest of the spike.
        ServiceLog(LogType.Warn, $"[256-spike] LogonComplete(v2) SessionKey={Convert.ToHexString(tmpSession.SessionKey)}");

        LogonCompleteNotification logonComplete = new()
        {
            ErrorCode = 0,
            Record = new LogonRecord
            {
                AccountId = tmpSession.AccountInfo.Id,
                SessionKey = ByteString.CopyFrom(tmpSession.SessionKey),
                // Deliberately no LoginTicket: TrinityCore leaves it unset here, and the client
                // already holds the ticket it just presented. Sending it back made no difference
                // to login itself, but it is the one field where we deviated from the reference.
            },
        };

        foreach (var gameAccount in tmpSession.AccountInfo.GameAccounts.Values)
        {
            logonComplete.Record.GameAccount.Add(new GameAccountHandle
            {
                Id = gameAccount.Id,
                TitleId = WowTitleId,
                Region = 2,
            });
        }

        _globalSession = tmpSession;

        // FIXME(256-spike): temporary diagnostics. Remove before any PR. Deliberately does not
        // print the record: it carries the session key, which must never reach a log file.
        ServiceLog(LogType.Warn,
            $"[256-spike] LogonComplete >>> accountId={logonComplete.Record.AccountId}, " +
            $"gameAccounts={logonComplete.Record.GameAccount.Count}");

        SendRequestAfterResponse(OriginalHash.AuthenticationListenerV2, 1, logonComplete);
        return BattlenetRpcErrorCode.Ok;
    }
}
