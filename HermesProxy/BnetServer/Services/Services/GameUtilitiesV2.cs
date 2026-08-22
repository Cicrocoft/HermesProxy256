// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Bgs.Protocol;
using Bgs.Protocol.GameUtilities.V1;
using Bgs.Protocol.GameUtilities.V2.Client;
using Framework.Constants;
using Framework.Logging;
using System;
using System.Collections.Generic;

namespace BNetServer.Services;

// Version 2 of the Battle.net game utilities service — the route modern clients take to reach the
// realm list. The commands and their handling are identical to v1; only the envelope differs, so
// this translates the v2 attributes into the v1 shapes and reuses the existing command handlers
// rather than duplicating them.
//
// Two differences are worth knowing:
//   * bgs.protocol.v2.Variant numbers its fields one lower than the v1 Variant, and drops the
//     message/fourcc/entity-id cases. Only the six common cases can cross over.
//   * v2 command names are matched without their trailing version suffix. v1 compares against a
//     per-expansion ending ("bcc1" for TBC); the suffix a modern client sends need not be one this
//     proxy knows, and the command before it is what actually identifies the request.
public partial class BnetServices
{
    [Service(ServiceRequirement.LoggedIn, OriginalHash.GameUtilitiesServiceV2, 1)]
    BattlenetRpcErrorCode HandleProcessTaskV2(ProcessTaskRequest request, ProcessTaskResponse response)
    {
        Dictionary<string, Variant> paramz = new();
        string? commandName = null;

        foreach (var attribute in request.Attribute)
        {
            if (attribute.Name == null)
                continue;

            paramz[attribute.Name] = ConvertVariantToV1(attribute.Value);
            if (attribute.Name.Contains("Command_"))
                commandName = attribute.Name;
        }

        if (commandName == null)
        {
            ServiceLog(LogType.Error, "Sent ProcessTaskRequest with no command.");
            return BattlenetRpcErrorCode.RpcMalformedRequest;
        }

        ServiceLog(LogType.Debug, $"GameUtilitiesService(v2) method: {commandName}");

        // FIXME(256-spike): temporary diagnostics. Remove before any PR.
        foreach (var (name, value) in paramz)
            ServiceLog(LogType.Warn, $"[256-spike]   param '{name}' blob={value.HasBlobValue} len={(value.HasBlobValue ? value.BlobValue.Length : 0)} str='{(value.HasStringValue ? value.StringValue : "")}'");

        ClientResponse v1Response = new();
        BattlenetRpcErrorCode result = DispatchCommand(StripVersionSuffix(commandName), paramz, v1Response);

        // FIXME(256-spike): temporary diagnostics. Remove before any PR.
        ServiceLog(LogType.Warn, $"[256-spike] {commandName} -> {result}, {v1Response.Attribute.Count} response attributes");

        foreach (var attribute in v1Response.Attribute)
        {
            response.Result.Add(new Bgs.Protocol.V2.Attribute
            {
                Name = attribute.Name,
                Value = ConvertVariantToV2(attribute.Value),
            });
        }

        return result;
    }

    [Service(ServiceRequirement.LoggedIn, OriginalHash.GameUtilitiesServiceV2, 2)]
    BattlenetRpcErrorCode HandleGetAllValuesForAttributeV2(Bgs.Protocol.GameUtilities.V2.Client.GetAllValuesForAttributeRequest request, Bgs.Protocol.GameUtilities.V2.Client.GetAllValuesForAttributeResponse response)
    {
        if (StripVersionSuffix(request.AttributeKey ?? "") != "Command_RealmListRequest_v1")
            return BattlenetRpcErrorCode.RpcNotImplemented;

        GetSession().AuthClient.WaitOrRequestRealmList();

        Bgs.Protocol.GameUtilities.V1.GetAllValuesForAttributeResponse v1Response = new();
        GetSession().RealmManager.WriteSubRegions(v1Response);

        foreach (var value in v1Response.AttributeValue)
            response.AttributeValue.Add(ConvertVariantToV2(value));

        return BattlenetRpcErrorCode.Ok;
    }

    /// <summary>
    /// Routes a suffix-stripped command to the same handlers the v1 service uses.
    /// </summary>
    private BattlenetRpcErrorCode DispatchCommand(string command, Dictionary<string, Variant> paramz, ClientResponse response)
    {
        switch (command)
        {
            case "Command_RealmListTicketRequest_v1": return GetRealmListTicket(paramz, response);
            case "Command_LastCharPlayedRequest_v1":  return GetLastCharPlayed(paramz, response);
            case "Command_RealmListRequest_v1":       return GetRealmList(paramz, response);
            case "Command_RealmJoinRequest_v1":       return JoinRealm(paramz, response);
        }

        // Answer Ok with no attributes rather than RpcNotImplemented, for the same reason the
        // service dispatcher does: this client treats an error here as something to retry rather
        // than something to accept. Build 69110 asks for Command_FetchBleepProxiesRequest_v1 — a
        // subsystem the proxy knows nothing about — and an error answer put it in a tight retry
        // loop, 374 requests in under a minute. An empty success is the truthful answer: we have
        // no proxies to offer.
        ServiceLog(LogType.Warn, $"Sent unhandled command '{command}' (v2) — answering Ok with no attributes.");
        return BattlenetRpcErrorCode.Ok;
    }

    /// <summary>
    /// Drops the trailing per-build suffix from a command name: "Command_X_v1_bcc1" becomes
    /// "Command_X_v1". Anything not shaped like a command is returned untouched.
    /// </summary>
    private static string StripVersionSuffix(string command)
    {
        if (!command.StartsWith("Command_"))
            return command;

        int lastUnderscore = command.LastIndexOf('_');
        return lastUnderscore > 0 ? command.Substring(0, lastUnderscore) : command;
    }

    private static Variant ConvertVariantToV1(Bgs.Protocol.V2.Variant? source)
    {
        Variant target = new();
        if (source == null)
            return target;

        if (source.HasBoolValue) target.BoolValue = source.BoolValue;
        if (source.HasIntValue) target.IntValue = source.IntValue;
        if (source.HasFloatValue) target.FloatValue = source.FloatValue;
        if (source.HasStringValue) target.StringValue = source.StringValue;
        if (source.HasBlobValue) target.BlobValue = source.BlobValue;
        if (source.HasUintValue) target.UintValue = source.UintValue;

        return target;
    }

    private static Bgs.Protocol.V2.Variant ConvertVariantToV2(Variant? source)
    {
        Bgs.Protocol.V2.Variant target = new();
        if (source == null)
            return target;

        if (source.HasBoolValue) target.BoolValue = source.BoolValue;
        if (source.HasIntValue) target.IntValue = source.IntValue;
        if (source.HasFloatValue) target.FloatValue = source.FloatValue;
        if (source.HasStringValue) target.StringValue = source.StringValue;
        if (source.HasBlobValue) target.BlobValue = source.BlobValue;
        if (source.HasUintValue) target.UintValue = source.UintValue;

        return target;
    }
}
