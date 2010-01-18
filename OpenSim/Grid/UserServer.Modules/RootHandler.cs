/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Reflection;
using System.Web;
using DotNetOpenAuth.Messaging;
using DotNetOpenAuth.OAuth.Messages;
using DotNetOpenAuth.OpenId;
using DotNetOpenAuth.OpenId.Extensions.AttributeExchange;
using DotNetOpenAuth.OpenId.Extensions.SimpleRegistration;
using DotNetOpenAuth.OpenId.Provider;
using DotNetOpenAuth.OpenId.RelyingParty;
using log4net;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenMetaverse;
using OpenMetaverse.Http;
using OpenMetaverse.StructuredData;
using CableBeachMessages;

namespace OpenSim.Grid.UserServer.Modules
{
    public class RootPageStreamHandler : IStreamHandler
    {
        const string XRDS_DOCUMENT =
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<xrds:XRDS xmlns:xrds=""xri://$xrds"" xmlns=""xri://$xrd*($v*2.0)"">
<XRD>
    <Service priority=""0"">
    <Type>http://specs.openid.net/auth/2.0/server</Type>
    <URI>{0}</URI>
    </Service>
</XRD>
</xrds:XRDS>
";

        public string ContentType { get { return null; } }
        public string HttpMethod { get { return m_httpMethod; } }
        public string Path { get { return m_path; } }

        string m_httpMethod;
        string m_path;
        UserLoginService m_loginService;

        /// <summary>
        /// Constructor
        /// </summary>
        public RootPageStreamHandler(string httpMethod, string path, UserLoginService loginService)
        {
            m_loginService = loginService;
            m_httpMethod = httpMethod;
            m_path = path;
        }

        /// <summary>
        /// Handles all GET and POST requests for Facebook Connect logins
        /// </summary>
        public void Handle(string path, Stream request, Stream response, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            Uri openidServerUrl = new Uri(httpRequest.Url, "/openid/server");
  
            if (OpenAuthHelper.IsXrdsRequest(httpRequest))
            {
                httpResponse.ContentType = "application/xrds+xml";
                OpenAuthHelper.AddToBody(httpResponse, String.Format(XRDS_DOCUMENT, openidServerUrl));
            }
            else
            {
                httpResponse.ContentType = "text/html";
                CableBeachState.SendRootTemplate(httpResponse, openidServerUrl);
            }
        }
    }
}
