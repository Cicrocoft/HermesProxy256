// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Framework.Cryptography;
using Framework.Logging;
using Framework.Networking;
using Framework.Serialization;
using Framework.Web;
using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using HermesProxy;
using HermesProxy.Auth;
using HermesProxy.Configuration.Options;
using HermesProxy.Enums;
using HermesProxy.World.Server;
using Microsoft.Extensions.Options;

namespace BNetServer.Networking;

public sealed class BnetRestApiSession : SSLSocket
{
    private const string BNET_SERVER_BASE_PATH = "/bnetserver/";
    private const string TICKET_PREFIX = "HP-"; // Hermes Proxy

    private readonly IOptions<ClientOptions> _clientOptions;
    private readonly IOptions<LegacyServerOptions> _legacyServerOptions;
    private readonly IOptions<ProxyNetworkOptions> _networkOptions;
    private readonly IOptions<DiagnosticsOptions> _diagnosticsOptions;
    private readonly IOptions<AccountOptions> _accountOptions;

    // SRP login spans two POSTs - the challenge, then the evidence - and the client is free to
    // make them on separate connections, so the in-flight exchange cannot live on this session.
    // Keyed by SRP username (hex SHA-256 of the account name); one entry per account at a time.
    private static readonly ConcurrentDictionary<string, PendingSrpLogin> _pendingSrpLogins = new();

    private sealed record PendingSrpLogin(BnetSRP6Base Srp, DateTime StartedUtc);

    // An abandoned challenge would otherwise pin a verifier forever.
    private static readonly TimeSpan SrpChallengeLifetime = TimeSpan.FromMinutes(5);

    public BnetRestApiSession(
        Socket socket,
        IOptions<ClientOptions> clientOptions,
        IOptions<LegacyServerOptions> legacyServerOptions,
        IOptions<ProxyNetworkOptions> networkOptions,
        IOptions<DiagnosticsOptions> diagnosticsOptions,
        IOptions<AccountOptions> accountOptions) : base(socket)
    {
        _accountOptions = accountOptions;
        _clientOptions = clientOptions;
        _legacyServerOptions = legacyServerOptions;
        _networkOptions = networkOptions;
        _diagnosticsOptions = diagnosticsOptions;
    }

    public override void Accept()
    {
        // FIXME(256-spike): temporary diagnostics. Distinguishes "client never dialled the REST
        // endpoint" from "client dialled it but rejected our certificate", which the router-level
        // log alone cannot tell apart because it sits behind the TLS handshake. Remove before any PR.
        Framework.Logging.Log.Print(Framework.Logging.LogType.Warn,
            $"[256-spike] REST accept from {GetRemoteIpEndPoint()} (tls={!_networkOptions.Value.RestPlaintext})");

        if (_networkOptions.Value.RestPlaintext)
        {
            AcceptPlaintext();
            return;
        }

        // Setup SSL connection
        AsyncHandshake(BnetServerCertificate.Certificate);
    }

    public override async Task ReadHandler(byte[] data, int receivedLength)
    {
        var httpRequest = HttpHelper.ParseRequest(data, receivedLength);
        if (httpRequest == null || !RequestRouter(httpRequest))
        {
            CloseSocket();
            return;
        }

        await AsyncRead(); // Read next request
    }

    public bool RequestRouter(HttpHeader httpRequest)
    {
        // FIXME(256-spike): temporary diagnostics. The REST layer logs nothing today, so a
        // client that asks for an unexpected path is indistinguishable from one that never
        // connected at all. Remove before any PR.
        Framework.Logging.Log.Print(Framework.Logging.LogType.Warn,
            $"[256-spike] REST {httpRequest.Method} {httpRequest.Path}");

        if (!httpRequest.Path!.StartsWith(BNET_SERVER_BASE_PATH))
        {
            _ = SendEmptyResponse(HttpCode.NotFound);
            return false;
        }

        string path = httpRequest.Path.Substring(BNET_SERVER_BASE_PATH.Length);
        string[] pathElements = path.Split('/');

        switch (pathElements[0], httpRequest.Method)
        {
            case ("login", "GET"):
                _ = SendResponse(HttpCode.Ok, LoginServiceManager.Instance.GetFormInput(GetRemoteIpEndPoint()!.Address));
                return true;
            case ("login", "POST") when pathElements.Length > 1 && pathElements[1] == "srp":
                _ = HandleSrpChallengeRequest(httpRequest);
                return true;
            case ("login", "POST"):
                _ = HandleLoginRequest(pathElements, httpRequest);
                return true;
            default:
                _ = SendEmptyResponse(HttpCode.NotFound);
                return false;
        };
    }

    /// <summary>
    /// First half of an SRP login: hand the client the parameters and our public B.
    /// </summary>
    /// <remarks>
    /// Salt and verifier normally come from an account database. This proxy has none - it forwards
    /// credentials to the emulator - so both are derived on the spot from the configured password.
    /// A client that proves knowledge of that password therefore proves it can log in upstream too.
    /// </remarks>
    public Task HandleSrpChallengeRequest(HttpHeader request)
    {
        LogonData? loginForm = Json.CreateObject<LogonData>(request.Content!);
        if (loginForm == null)
            return SendEmptyResponse(HttpCode.InternalServerError);

        string login = (loginForm["account_name"] ?? "").Trim().ToUpperInvariant();
        if (login.IsEmpty())
            return SendAuthError(AuthResult.FAIL_UNKNOWN_ACCOUNT);

        string configuredPassword = _accountOptions.Value.Password;
        if (configuredPassword.IsEmpty())
        {
            Log.Print(LogType.Error, "SRP login was requested but AccountOptions.Password is not set. " +
                "Modern clients never send the password, so the proxy cannot reach the legacy server without it.");
            return SendAuthError(AuthResult.FAIL_INTERNAL_ERROR);
        }

        string srpUsername = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(login)));

        // FIXME(256-spike): temporary diagnostics. Remove before any PR. Prints a fingerprint of
        // the configured password rather than the password, so a mismatch between what the proxy
        // was started with and what the client is proving knowledge of can be told apart from a
        // genuine typo without either value appearing in a log.
        Log.Print(LogType.Warn,
            $"[256-spike] SRP challenge: accountName='{login}', srpVersion={_accountOptions.Value.SrpVersion}, " +
            $"configuredPassword length={configuredPassword.Length} " +
            $"sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configuredPassword)))[..16]}");

        BnetSRP6Base srp = CreateSrpImplementation(srpUsername, configuredPassword);

        PruneExpiredSrpLogins();
        _pendingSrpLogins[srpUsername] = new PendingSrpLogin(srp, DateTime.UtcNow);

        SrpLoginChallenge challenge = new()
        {
            Version = srp.GetVersion(),
            Iterations = (int)srp.GetXIterations(),
            Modulus = Convert.ToHexString(srp.GetN().ToByteArray(true, true)),
            Generator = Convert.ToHexString(srp.Getg().ToByteArray(true, true)),
            HashFunction = "SHA-256",
            Username = srpUsername,
            Salt = Convert.ToHexString(srp.s),
            PublicB = Convert.ToHexString(srp.B.ToByteArray(true, true)),
        };

        return SendResponse(HttpCode.Ok, challenge);
    }

    /// <summary>
    /// Builds the SRP verifier for this account from the configured password, so the client's proof
    /// can be checked against it.
    /// </summary>
    private BnetSRP6Base CreateSrpImplementation(string srpUsername, string password)
    {
        // v1 upper-cases the password before hashing; v2 takes it as typed.
        if (_accountOptions.Value.SrpVersion == 1)
        {
            var registration = new BnetSRP6v1Hash256();
            byte[] verifier = registration.CalculateVerifier(srpUsername, password.ToUpperInvariant(), registration.s);
            return new BnetSRP6v1Hash256(srpUsername, registration.s, verifier);
        }
        else
        {
            // Reject salts on which our reading of x and the client's can differ; see
            // BnetSRP6v2Base.XReadingIsAmbiguous. Half of all salts qualify, so this almost always
            // accepts the first or second - the bound only stops a pathological spin.
            var registration = new BnetSRP6v2Hash256();
            int rejected = 0;
            while (rejected < 32 &&
                   BnetSRP6v2Base.XReadingIsAmbiguous(srpUsername, password, registration.s, registration.GetXIterations()))
            {
                registration = new BnetSRP6v2Hash256();
                rejected++;
            }
            if (rejected > 0)
                Log.Print(LogType.Debug, $"SRP: skipped {rejected} salt(s) with an ambiguous reading of x.");

            byte[] verifier = registration.CalculateVerifier(srpUsername, password, registration.s);
            return new BnetSRP6v2Hash256(srpUsername, registration.s, verifier);
        }
    }

    private static void PruneExpiredSrpLogins()
    {
        DateTime cutoff = DateTime.UtcNow - SrpChallengeLifetime;
        foreach (var entry in _pendingSrpLogins)
            if (entry.Value.StartedUtc < cutoff)
                _pendingSrpLogins.TryRemove(entry.Key, out _);
    }

    public Task HandleLoginRequest(string[] pathElements, HttpHeader request)
    {
        LogonData? loginForm = Json.CreateObject<LogonData>(request.Content!);
        if (loginForm == null)
            return SendEmptyResponse(HttpCode.InternalServerError);

        HermesProxy.GlobalSessionData globalSession = new(_clientOptions.Value, _legacyServerOptions.Value, _networkOptions.Value, _diagnosticsOptions.Value);

        // Format: "login/$platform/$build/$locale/"
        globalSession.OS = pathElements[1];
        globalSession.Build = uint.Parse(pathElements[2]);
        globalSession.Locale = pathElements[3];

        // Should never happen. Session.HandleLogon checks version already
        if (ModernVersion.Build != (ClientVersionBuild) globalSession.Build)
            return SendAuthError(AuthResult.FAIL_WRONG_MODERN_VER);

        string login = "";
        string password = "";

        foreach (var field in loginForm.Inputs)
        {
            switch (field.Id)
            {
                case "account_name": login = field.Value!.Trim().ToUpperInvariant(); break;
                case "password": password = field.Value!.Trim(); break;
            }
        }

        // Modern clients complete the SRP exchange started at /bnetserver/login/srp/ instead of
        // sending a password. Verifying their evidence proves they know the configured password,
        // which is then what we use upstream - the legacy handshake needs it in the clear.
        string? serverEvidenceM2 = null;
        string? publicA = loginForm["public_A"];
        string? clientEvidenceM1 = loginForm["client_evidence_M1"];
        if (!string.IsNullOrEmpty(publicA) && !string.IsNullOrEmpty(clientEvidenceM1))
        {
            string srpUsername = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(login)));
            if (!_pendingSrpLogins.TryRemove(srpUsername, out var pending))
            {
                Log.Print(LogType.Error, $"SRP evidence arrived for '{login}' without a matching challenge.");
                return SendAuthError(AuthResult.FAIL_INTERNAL_ERROR);
            }

            BigInteger A = new(publicA!.ToByteArray(), true, true);
            BigInteger M1 = new(clientEvidenceM1!.ToByteArray(), true, true);

            BigInteger? sessionKey = pending.Srp.VerifyClientEvidence(A, M1);
            if (sessionKey == null)
                return SendAuthError(AuthResult.FAIL_INCORRECT_PASSWORD);

            serverEvidenceM2 = Convert.ToHexString(
                pending.Srp.CalculateServerEvidence(A, M1, sessionKey.Value).ToByteArray(true, true));

            password = _accountOptions.Value.Password;
        }

        globalSession.AuthClient = new(globalSession);
        AuthResult response = globalSession.AuthClient.ConnectToAuthServer(login, password, globalSession.Locale);
        if (response != AuthResult.SUCCESS)
        { // Error handling
            return SendAuthError(response);
        }
        else
        {
            // Request realmlist now, we probably need it later anyways
            globalSession.AuthClient.SendRealmListUpdateRequest();

            // Ticket creation
            LogonResult loginResult = new();
            byte[] ticket = Array.Empty<byte>().GenerateRandomKey(20);
            string loginTicket = TICKET_PREFIX + ticket.ToHexString();

            globalSession.LoginTicket = loginTicket;
            globalSession.Username = login;
            globalSession.AccountMetaDataMgr = new AccountMetaDataManager(login);
            BnetSessionTicketStorage.AddNewSessionByName(login, globalSession);
            BnetSessionTicketStorage.AddNewSessionByTicket(loginTicket, globalSession);

            loginResult.LoginTicket = loginTicket;
            loginResult.AuthenticationState = "DONE";
            loginResult.ServerEvidenceM2 = serverEvidenceM2;
            return SendResponse(HttpCode.Ok, loginResult);
        }
    }

    async Task SendResponse<T>(HttpCode code, T response)
    {
        await AsyncWrite(HttpHelper.CreateResponse(code, Json.CreateString(response)));
    }

    async Task SendAuthError(AuthResult response)
    {
        // FIXME(256-spike): temporary diagnostics. Every login failure path funnels through here
        // and none of them logged, so a rejected password looked identical to an unreachable
        // logon server. Remove before any PR.
        Log.Print(LogType.Warn, $"[256-spike] login rejected: {response}");

        LogonResult loginResult = new();
        (loginResult.AuthenticationState, loginResult.ErrorCode, loginResult.ErrorMessage) = response switch
        {
            AuthResult.FAIL_UNKNOWN_ACCOUNT    => ("LOGIN", "UNABLE_TO_DECODE", "Invalid username or password."),
            AuthResult.FAIL_INCORRECT_PASSWORD => ("LOGIN", "UNABLE_TO_DECODE", "Invalid password."),
            AuthResult.FAIL_BANNED             => ("LOGIN", "UNABLE_TO_DECODE", "This account has been closed and is no longer available for use."),
            AuthResult.FAIL_SUSPENDED          => ("LOGIN", "UNABLE_TO_DECODE", "This account has been temporarily suspended."),
            AuthResult.FAIL_VERSION_INVALID    => ("LOGIN", "UNABLE_TO_DECODE", "Your version is not supported by this server.\nMake sure you are using the latest HermesProxy version from GitHub.\n(Maybe HermesProxy is blocked on the server)\n"),

            AuthResult.FAIL_INTERNAL_ERROR     => ("LOGON", "UNABLE_TO_DECODE", "There was an internal error. Please try again later."),
            _ => ("LOGON", "UNABLE_TO_DECODE", $"Error: {response}"),
        };

        await SendResponse(HttpCode.BadRequest, loginResult);
    }

    async Task SendEmptyResponse(HttpCode code)
    {
        await SendResponse<object>(code, new{});
    }
}
