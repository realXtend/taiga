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
using System.IO;
using System.Net;
using System.Text;
using log4net;
using Nini.Config;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Handlers.Base;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using CableBeachMessages;

namespace ModCableBeach
{
    public class CableBeachServerConnector : ServiceConnector
    {
        public CableBeachServerConnector(IConfigSource config, IHttpServer server) :
            base(config, server)
        {
            // Front page of the inventory server and the XRD document
            server.AddStreamHandler(new CBRootPageHandler("GET", "/"));
            server.AddStreamHandler(new CBXrdDocumentHandler("GET", "/xrd"));

            // Seed capability endpoint
            server.AddStreamHandler(new TrustedStreamHandler("POST", "/request_capabilities", new CBRequestCapabilitiesHandler()));

            // Handler for all dynamically generated capabilities
            server.AddStreamHandler(new CapabilityStreamHandler("POST", CableBeachServerState.CABLE_BEACH_CAPS_PATH));

            // OAuth endpoints
            CBOAuthGetRequestTokenHandler getRequestTokenHandler = new CBOAuthGetRequestTokenHandler("GET", "/oauth/get_request_token");
            CBOAuthAuthorizeTokenHandler authorizeTokenHandler = new CBOAuthAuthorizeTokenHandler("GET", "/oauth/authorize_token");
            CBOAuthGetAccessTokenHandler getAccessTokenHandler = new CBOAuthGetAccessTokenHandler("GET", "/oauth/get_access_token");

            server.AddStreamHandler(new CBOAuthGetRequestTokenHandler("GET", "/oauth/get_request_token"));
            server.AddStreamHandler(new CBOAuthAuthorizeTokenHandler("GET", "/oauth/authorize_token"));
            server.AddStreamHandler(new CBOAuthGetAccessTokenHandler("GET", "/oauth/get_access_token"));
        }
    }

    public class CapabilityStreamHandler : BaseStreamHandler
    {
        public CapabilityStreamHandler(string httpMethod, string path) :
            base(httpMethod, path)
        {
        }

        public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            UUID requestID = UUID.Zero;

            // Get the capability ID from the request URL
            path = httpRequest.Url.AbsolutePath.TrimEnd('/');
            if (path.Length > 36)
                UUID.TryParse(path.Substring(path.Length - 36), out requestID);

            // Try and lookup the capability ID in the capabilities dictionary
            Capability cap;
            if (CableBeachServerState.Capabilities.TryGetValue(requestID, out cap))
            {
                return cap.HttpHandler.Handle(path, request, httpRequest, httpResponse);
            }
            else
            {
                CableBeachServerState.Log.Warn("[CABLE BEACH SERVER]: Request for missing capability: " + httpRequest.Url);
                httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                return Utils.EmptyBytes;
            }
        }
    }

    public class CBRootPageHandler : BaseStreamHandler
    {
        public CBRootPageHandler(string httpMethod, string path) :
            base(httpMethod, path)
        {
        }

        public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            httpResponse.ContentType = "text/html";
            return CableBeachServerState.BuildInventoryRootPageTemplate(new Uri(httpRequest.Url, "/xrd").ToString());
        }
    }

    public class CBXrdDocumentHandler : BaseStreamHandler
    {
        public CBXrdDocumentHandler(string httpMethod, string path) :
            base(httpMethod, path)
        {
        }

        public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            // Create an XRD document describing the service endpoints of this server
            XrdDocument xrd = new XrdDocument(new Uri(httpRequest.Url, "/").ToString());
            xrd.Types.Add(CableBeachServices.ASSETS);
            xrd.Types.Add(CableBeachServices.FILESYSTEM);

            xrd.Links.Add(new XrdLink(new Uri(CableBeachServices.SEED_CAPABILITY), "application/json", new XrdUri(new Uri(httpRequest.Url, "/request_capabilities"))));
            xrd.Links.Add(new XrdLink(new Uri(CableBeachServices.OAUTH_INITIATE), null, new XrdUri(new Uri(httpRequest.Url, "/oauth/get_request_token"))));
            xrd.Links.Add(new XrdLink(new Uri(CableBeachServices.OAUTH_AUTHORIZE), null, new XrdUri(new Uri(httpRequest.Url, "/oauth/authorize_token"))));
            xrd.Links.Add(new XrdLink(new Uri(CableBeachServices.OAUTH_TOKEN), null, new XrdUri(new Uri(httpRequest.Url, "/oauth/get_access_token"))));

            httpResponse.ContentType = "application/xrd+xml";
            return Encoding.UTF8.GetBytes(XrdParser.WriteXrd(xrd));
        }
    }

    public class CBRequestCapabilitiesHandler : BaseStreamHandler
    {
        public CBRequestCapabilitiesHandler() :
            base(null, null)
        {
        }

        public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            OSDMap requestMap;
            if (ServiceHelper.TryGetOSD(httpRequest, out requestMap))
            {
                RequestCapabilitiesMessage message = new RequestCapabilitiesMessage();
                message.Deserialize(requestMap);

                RequestCapabilitiesReplyMessage reply = new RequestCapabilitiesReplyMessage();

                CableBeachServerState.Log.Info("[CABLE BEACH SERVER]: Handling request for " + message.Capabilities.Length +
                    " capabilities from " + httpRequest.RemoteIPEndPoint.Address);

                // Create a dictionary of requested capabilities
                Dictionary<Uri, Uri> capabilities = new Dictionary<Uri, Uri>(message.Capabilities.Length);
                for (int i = 0; i < message.Capabilities.Length; i++)
                    capabilities[message.Capabilities[i]] = null;

                // Allow each registered service to attempt to fill in the capabilities request
                CableBeachServerState.CreateCapabilities(httpRequest.Url, message.Identity, ref capabilities);

                // Convert the dictionary of created capabilities to a reply
                reply.Capabilities = new Dictionary<Uri, Uri>(capabilities.Count);
                foreach (KeyValuePair<Uri, Uri> entry in capabilities)
                {
                    if (entry.Value != null)
                        reply.Capabilities[entry.Key] = entry.Value;
                    else
                        CableBeachServerState.Log.Warn("[CABLE BEACH SERVER]: Capability was not created for service identifier " + entry.Key);
                }

                CableBeachServerState.Log.Info("[CABLE BEACH SERVER]: Returning " + reply.Capabilities.Count +
                    " capabilities to " + httpRequest.RemoteIPEndPoint.Address);

                return ServiceHelper.MakeResponse(httpResponse, reply.Serialize());
            }
            else
            {
                CableBeachServerState.Log.Error("[CABLE BEACH SERVER]: request_capabilities called with invalid data");
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return Utils.EmptyBytes;
            }


        }
    }

    public class CBOAuthGetRequestTokenHandler : BaseStreamHandler
    {
        public CBOAuthGetRequestTokenHandler(string httpMethod, string path) :
            base(httpMethod, path)
        {
        }

        public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
        }
    }

    public class CBOAuthAuthorizeTokenHandler : BaseStreamHandler
    {
        public CBOAuthAuthorizeTokenHandler(string httpMethod, string path) :
            base(httpMethod, path)
        {
        }

        public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
        }
    }

    public class CBOAuthGetAccessTokenHandler : BaseStreamHandler
    {
        public CBOAuthGetAccessTokenHandler(string httpMethod, string path) :
            base(httpMethod, path)
        {
        }

        public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
        }
    }
}
