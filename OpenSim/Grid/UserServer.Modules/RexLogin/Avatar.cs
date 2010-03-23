/* 
 * Copyright (c) Intel Corporation
 * All rights reserved.
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions
 * are met:
 *
 * -- Redistributions of source code must retain the above copyright
 *    notice, this list of conditions and the following disclaimer.
 * -- Redistributions in binary form must reproduce the above copyright
 *    notice, this list of conditions and the following disclaimer in the
 *    documentation and/or other materials provided with the distribution.
 * -- Neither the name of the Intel Corporation nor the names of its
 *    contributors may be used to endorse or promote products derived from
 *    this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
 * ``AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
 * LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A
 * PARTICULAR PURPOSE ARE DISCLAIMED.  IN NO EVENT SHALL THE INTEL OR ITS
 * CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
 * EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
 * PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
 * PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
 * LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
 * NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Net;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace OpenSim.Grid.UserServer.Modules.RexLogin
{

    public class Avatar
    {
        public Uri Identity;
        public UUID ID;
        public Dictionary<Uri, OSD> Attributes;
        public ServiceCollection Services;

        public Avatar()
        {
            Identity = null;
            ID = UUID.Zero;
            Attributes = new Dictionary<Uri, OSD>();
            Services = new ServiceCollection();
        }

        public Avatar(Uri identifier, UUID id, Dictionary<Uri, OSD> attributes, ServiceCollection services)
        {
            Identity = identifier;
            ID = id;
            Attributes = attributes;
            Services = services;
        }

        public OSD GetAttribute(Uri attributeIdentifier)
        {
            OSD value;
            if (Attributes.TryGetValue(attributeIdentifier, out value))
                return value;
            else
                return new OSD();
        }

        public OSDMap Serialize()
        {
            OSDMap map = new OSDMap(3);
            map["ID"] = OSD.FromUUID(ID);
            map["Identifier"] = OSD.FromUri(Identity);

            OSDMap dict = new OSDMap(Attributes.Count);
            foreach (KeyValuePair<Uri, OSD> entry in Attributes)
                dict.Add(entry.Key.ToString(), entry.Value);
            map["Attributes"] = dict;

            return map;
        }

        public void Deserialize(OSDMap map)
        {
            ID = map["ID"].AsUUID();
            Identity = map["Identifier"].AsUri();

            OSDMap dict = (OSDMap)map["Attributes"];
            Attributes = new Dictionary<Uri, OSD>(dict.Count);
            foreach (KeyValuePair<string, OSD> entry in dict)
                Attributes.Add(new Uri(entry.Key), entry.Value);
        }
    }
}
