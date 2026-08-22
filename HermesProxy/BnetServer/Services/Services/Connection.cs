// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Bgs.Protocol;
using Google.Protobuf;
using Bgs.Protocol.Connection.V1;
using Framework.Constants;
using System;

namespace BNetServer.Services;

public partial class BnetServices
{
    // Monotonic stand-in for TrinityCore's per-session id, used to mint a ClientId for clients
    // that don't supply one. Only needs to be unique per proxy run.
    private static uint _connectClientIdCounter;

    [Service(ServiceRequirement.Unauthorized, OriginalHash.ConnectionService, 1)]
    BattlenetRpcErrorCode HandleConnect(ConnectRequest request, ConnectResponse response)
    {
        // Modern (5.5.0-engine) clients send a bare ConnectRequest with no ClientId. Current
        // TrinityCore assigns one server-side in that case; without it the client treats the
        // handshake as failed and gives up before it ever sends AuthenticationService.Logon.
        //
        // Gated on the build. This is the first response of every session, and the clients that
        // already worked demonstrably send no ClientId either — the old MergeFrom here would have
        // thrown on a fresh ConnectResponse, so that branch was never reached. They therefore used
        // to receive a response with no client_id at all, and handing them an unsolicited one is a
        // change to packet one of a working handshake for no reason.
        bool modernHandshake = HermesProxy.ModernVersion.Build == HermesProxy.Enums.ClientVersionBuild.V2_5_6_69110;

        if (request.ClientId != null)
        {
            response.ClientId = request.ClientId.Clone();
        }
        else if (modernHandshake)
        {
            response.ClientId = new ProcessId
            {
                Label = System.Threading.Interlocked.Increment(ref _connectClientIdCounter),
                Epoch = (uint)Time.UnixTime,
            };
        }

        response.ServerId = new ProcessId();
        response.ServerId.Label = (uint)Environment.ProcessId;
        response.ServerId.Epoch = (uint)Time.UnixTime;
        response.ServerTime = (ulong)Time.UnixTimeMilliseconds;

        response.UseBindlessRpc = request.UseBindlessRpc;

        // ciid (ConnectResponse field 9) postdates the protobuf snapshot vendored in
        // Framework/Proto, so there is no generated property for it. Protobuf preserves and
        // re-serializes unknown fields, so writing the tag by hand puts the same bytes on the
        // wire as current TrinityCore. Regenerating Framework/Proto from an up-to-date
        // connection_service.proto is the proper fix before this becomes a PR.
        //
        // Gated for the same reason as the ClientId above: older clients parse this response with
        // a descriptor that stops at field 8, and there is no way to tell from here whether their
        // parser skips an unknown field 9 or rejects it.
        if (modernHandshake)
        {
            string ciid = $"{response.ServerId.Label:X8}{response.ServerId.Epoch:X8}-{response.ClientId.Label:X8}{response.ClientId.Epoch:X8}";
            using (var ciidStream = new System.IO.MemoryStream())
            {
                var ciidWriter = new CodedOutputStream(ciidStream);
                ciidWriter.WriteTag(9, WireFormat.WireType.LengthDelimited);
                ciidWriter.WriteString(ciid);
                ciidWriter.Flush();
                response.MergeFrom(ciidStream.ToArray());
            }

            // FIXME(256-spike): temporary diagnostics. Remove before any PR.
            Framework.Logging.Log.Print(Framework.Logging.LogType.Warn, $"[256-spike] ciid={ciid}");
        }

        // FIXME(256-spike): temporary diagnostics while probing what the 2.5.6 (5.5.0-engine)
        // client expects from the BNet handshake. Remove before any PR.
        Framework.Logging.Log.Print(Framework.Logging.LogType.Warn,
            $"[256-spike] ConnectRequest  >>> {request}");
        Framework.Logging.Log.Print(Framework.Logging.LogType.Warn,
            $"[256-spike] ConnectResponse <<< {response}");

        return BattlenetRpcErrorCode.Ok;
    }

    [Service(ServiceRequirement.Always, OriginalHash.ConnectionService, 5)]
    BattlenetRpcErrorCode HandleKeepAlive(NoData request)
    {
        return BattlenetRpcErrorCode.Ok;
    }

    [Service(ServiceRequirement.Always, OriginalHash.ConnectionService, 7)]
    BattlenetRpcErrorCode HandleRequestDisconnect(DisconnectRequest request)
    {
        // FIXME(256-spike): temporary diagnostics. The client sends its reason here; without it
        // a client-initiated disconnect is indistinguishable from a normal logout.
        Framework.Logging.Log.Print(Framework.Logging.LogType.Warn,
            $"[256-spike] Client requested disconnect, ErrorCode={request.ErrorCode} (0x{request.ErrorCode:X})");

        if (GetSession() != null && GetSession().AuthClient != null)
            GetSession().AuthClient.Disconnect();

        var disconnectNotification = new DisconnectNotification();
        disconnectNotification.ErrorCode = request.ErrorCode;
        SendRequest(OriginalHash.ConnectionService, 4, disconnectNotification);

        CloseSocket();

        return BattlenetRpcErrorCode.Ok;
    }
}
