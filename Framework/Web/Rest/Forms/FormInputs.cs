// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Framework.Web;

[DataContract]
public class FormInputs
{
    [DataMember(Name = "type")]
    public string? Type { get; set; }

    [DataMember(Name = "prompt")]
    public string? Prompt { get; set; }

    [DataMember(Name = "inputs")]
    public List<FormInput> Inputs { get; set; } = new List<FormInput>();

    /// <summary>
    /// Where a client should POST its SRP challenge. Modern clients read this instead of posting
    /// the password to the form URL, and abandon login outright when it is missing.
    /// </summary>
    [DataMember(Name = "srp_url")]
    public string? SrpUrl { get; set; }

    /// <summary>Optional script the client may fetch to perform SRP. Unused here.</summary>
    [DataMember(Name = "srp_js")]
    public string? SrpJs { get; set; }
}
