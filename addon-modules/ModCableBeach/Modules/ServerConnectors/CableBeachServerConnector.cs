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
using System.Text;
using System.Web;
using log4net;
using Nini.Config;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Handlers.Base;
using DotNetOpenAuth.Messaging;
using DotNetOpenAuth.OAuth;
using DotNetOpenAuth.OAuth.ChannelElements;
using DotNetOpenAuth.OAuth.Messages;
using DotNetOpenAuth.OpenId;
using DotNetOpenAuth.OpenId.ChannelElements;
using DotNetOpenAuth.OpenId.RelyingParty;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using CableBeachMessages;

using OAuthServiceProvider = DotNetOpenAuth.OAuth.ServiceProvider;

namespace ModCableBeach
{
    public class CableBeachServerConnector : ServiceConnector
    {
        public CableBeachServerConnector(IConfigSource config, IHttpServer server) :
            base(config, server, "CableBeachService")
        {
            #region Config Loading

            IConfig serverConfig = config.Configs["CableBeachService"];
            if (serverConfig == null)
                throw new Exception("No CableBeachService section in config file");

            string serviceUrl = serverConfig.GetString("ServiceUrl", String.Empty);
            if (String.IsNullOrEmpty(serviceUrl))
                throw new Exception("No ServiceUrl in CableBeachService section in config file");
            if (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out CableBeachServerState.ServiceUrl))
                throw new Exception("Invalid ServiceUrl in CableBeachService section in config file");

            string openidProvider = serverConfig.GetString("OpenIDProvider", String.Empty);
            if (String.IsNullOrEmpty(openidProvider))
                throw new Exception("No OpenIDProvider in CableBeachService section in config file");
            if (!Uri.TryCreate(openidProvider, UriKind.Absolute, out CableBeachServerState.OpenIDProviderUrl))
                throw new Exception("Invalid OpenIDProvider in CableBeachService section in config file");

            CableBeachServerState.ServiceRootTemplateFile = serverConfig.GetString("RootPageTemplateFile", String.Empty);
            if (String.IsNullOrEmpty(CableBeachServerState.ServiceRootTemplateFile))
                throw new Exception("No RootPageTemplateFile in CableBeachService section in config file");

            CableBeachServerState.PermissionGrantTemplateFile = serverConfig.GetString("PermissionGrantTemplateFile", String.Empty);
            if (String.IsNullOrEmpty(CableBeachServerState.PermissionGrantTemplateFile))
                throw new Exception("No PermissionGrantTemplateFile in CableBeachService section in config file");

            #endregion Config Loading

            // Initialize the OAuth ServiceProvider for this set of services
            CableBeachServerState.OAuthServiceProvider = new OAuthServiceProvider(
                OpenAuthHelper.CreateServiceProviderDescription(CableBeachServerState.ServiceUrl), CableBeachServerState.OAuthTokenManager);

            #region HTTP Handlers

            // Front page of the service and the XRD document
            server.AddStreamHandler(new CBRootPageHandler("GET", "/"));
            server.AddStreamHandler(new CBXrdDocumentHandler("GET", "/xrd"));

            // Seed capability endpoint
            server.AddStreamHandler(new TrustedStreamHandler("POST", "/request_capabilities", new CBRequestCapabilitiesHandler()));

            // Handler for all dynamically generated capabilities
            server.AddStreamHandler(new CapabilityStreamHandler("POST", CableBeachServerState.CABLE_BEACH_CAPS_PATH));

            // OAuth endpoints
            server.AddStreamHandler(new CBOAuthGetRequestTokenHandler("GET", "/oauth/get_request_token"));
            server.AddStreamHandler(new CBOAuthAuthorizeTokenHandler("GET", "/oauth/authorize_token"));
            server.AddStreamHandler(new CBOAuthGetAccessTokenHandler("GET", "/oauth/get_access_token"));

            server.AddStreamHandler(new CBOAuthGetRequestTokenHandler("POST", "/oauth/get_request_token"));
            server.AddStreamHandler(new CBOAuthAuthorizeTokenHandler("POST", "/oauth/authorize_token"));
            server.AddStreamHandler(new CBOAuthGetAccessTokenHandler("POST", "/oauth/get_access_token"));

            // OpenID callback endpoint
            server.AddStreamHandler(new CBOpenIDCallbackHandler("GET", "/oauth/openid_callback"));

            // Permission grant form endpoint
            server.AddStreamHandler(new CBPermissionGrantFormHandler("POST", "/oauth/user_authorize"));

            #endregion HTTP Handlers

            CableBeachServerState.Log.Info("[CABLE BEACH SERVER]: CableBeachServerConnector is running");
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
                if (cap.ClientCertRequired)
                {
                    // TODO: Implement this
                }

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
            Uri xrdUrl = new Uri(httpRequest.Url, "/xrd");
            httpResponse.AddHeader("link", String.Format("<{0}>; rel=\"describedby\"; type=\"application/xrd+xml\"", xrdUrl));

            httpResponse.ContentType = "text/html";
            return CableBeachServerState.BuildServiceRootPageTemplate(new Uri(httpRequest.Url, "/xrd").ToString());
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
            Uri serviceUrl = CableBeachServerState.ServiceUrl;

            // Create an XRD document describing the service endpoints of this server
            XrdDocument xrd = new XrdDocument(serviceUrl.ToString());
            xrd.Types.Add(CableBeachServices.ASSETS);
            xrd.Types.Add(CableBeachServices.FILESYSTEM);

            xrd.Links.Add(new XrdLink(new Uri(CableBeachServices.SEED_CAPABILITY), "application/json", new XrdUri(new Uri(serviceUrl, "/request_capabilities"))));
            xrd.Links.Add(new XrdLink(new Uri(CableBeachServices.OAUTH_INITIATE), null, new XrdUri(new Uri(serviceUrl, "/oauth/get_request_token"))));
            xrd.Links.Add(new XrdLink(new Uri(CableBeachServices.OAUTH_AUTHORIZE), null, new XrdUri(new Uri(serviceUrl, "/oauth/authorize_token"))));
            xrd.Links.Add(new XrdLink(new Uri(CableBeachServices.OAUTH_TOKEN), null, new XrdUri(new Uri(serviceUrl, "/oauth/get_access_token"))));

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

    #region OAuth Endpoints

    public class CBOAuthGetRequestTokenHandler : BaseStreamHandler
    {
        public CBOAuthGetRequestTokenHandler(string httpMethod, string path) :
            base(httpMethod, path)
        {
        }

        public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            UnauthorizedTokenRequest tokenRequest = null;
            try { tokenRequest = CableBeachServerState.OAuthServiceProvider.ReadTokenRequest(OpenAuthHelper.GetRequestInfo(httpRequest)); }
            catch (Exception ex) { CableBeachServerState.Log.Error("[CABLE BEACH SERVER]: Error parsing get_request_token request: " + ex.Message, ex); }

            if (tokenRequest != null)
            {
                // TODO: If we wanted to support whitelisting/blacklisting worlds, we could do so here with
                // tokenRequest.ConsumerKey

                UnauthorizedTokenResponse tokenResponse = CableBeachServerState.OAuthServiceProvider.PrepareUnauthorizedTokenMessage(tokenRequest);
                return OpenAuthHelper.MakeOpenAuthResponse(httpResponse, CableBeachServerState.OAuthServiceProvider.Channel.PrepareResponse(tokenResponse));
            }
            else
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return Utils.EmptyBytes;
            }
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
            HttpRequestInfo requestInfo = OpenAuthHelper.GetRequestInfo(httpRequest);

            try
            {
                UserAuthorizationRequest oauthRequest = CableBeachServerState.OAuthServiceProvider.ReadAuthorizationRequest(requestInfo);

                if (oauthRequest != null && oauthRequest.ExtraData != null)
                {
                    IServiceProviderRequestToken requestToken = CableBeachServerState.OAuthTokenManager.GetRequestToken(oauthRequest.RequestToken);
                    if (requestToken.Callback != null)
                        oauthRequest.Callback = requestToken.Callback;

                    if (oauthRequest.Callback != null)
                    {
                        string capNameList;
                        if (oauthRequest.ExtraData.TryGetValue("cb_capabilities", out capNameList))
                        {
                            // Store the OAuth request state in a temporary dictionary to reference later
                            string[] capNames = capNameList.Split(',');
                            OAuthRequest thisRequest = new OAuthRequest(null, oauthRequest, capNames);
                            CableBeachServerState.OAuthCurrentRequests.AddOrUpdate(oauthRequest.RequestToken, thisRequest,
                                TimeSpan.FromMinutes(CableBeachServerState.OAUTH_OPENID_LOGIN_TIMEOUT_MINUTES));

                            try
                            {
                                // Redirect the user to do an OpenID login through our trusted identity provider
                                Realm realm = new Realm(new Uri(httpRequest.Url, "/"));
                                Identifier identifier;
                                Identifier.TryParse(CableBeachServerState.OpenIDProviderUrl.ToString(), out identifier);

                                IAuthenticationRequest authRequest = CableBeachServerState.OpenIDRelyingParty.CreateRequest(
                                    identifier, realm, new Uri(httpRequest.Url, "/oauth/openid_callback"));
                                authRequest.AddCallbackArguments("oauth_request_token", oauthRequest.RequestToken);
                                return OpenAuthHelper.MakeOpenAuthResponse(httpResponse, authRequest.RedirectingResponse);
                            }
                            catch (Exception ex)
                            {
                                CableBeachServerState.Log.Error("[CABLE BEACH SERVER]: OpenID authentication failed: " + ex.Message, ex);
                                httpResponse.StatusCode = (int)HttpStatusCode.InternalServerError;
                                return Encoding.UTF8.GetBytes("OpenID authentication failed: " + ex.Message);
                            }
                        }
                        else
                        {
                            // No capabilities were requested
                            CableBeachServerState.Log.Error("[CABLE BEACH SERVER]: Got an OAuth request with no capabilities being requested");
                            httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                            return Encoding.UTF8.GetBytes("Unknown capabilities");
                        }
                    }
                    else
                    {
                        // No callback was given
                        CableBeachServerState.Log.Error("[CABLE BEACH SERVER]: Got an OAuth request with no callback");
                        httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                        return Encoding.UTF8.GetBytes("Missing or invalid OAuth callback");
                    }
                }
                else
                {
                    CableBeachServerState.Log.Error("[CABLE BEACH SERVER]: authorize_token called with missing or invalid OAuth request");
                    httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                    return Encoding.UTF8.GetBytes("Missing or invalid OAuth request");
                }
            }
            catch (Exception ex)
            {
                CableBeachServerState.Log.Error("[CABLE BEACH SERVER]: authorize_token called with invalid data");
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return Encoding.UTF8.GetBytes("Failed to handle OAuth request: " + ex.Message);
            }
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
            AuthorizedTokenRequest tokenRequest = null;
            try { tokenRequest = CableBeachServerState.OAuthServiceProvider.ReadAccessTokenRequest(OpenAuthHelper.GetRequestInfo(httpRequest)); }
            catch (Exception ex) { CableBeachServerState.Log.Error("[CABLE BEACH SERVER]: Error parsing get_access_token request: " + ex.Message, ex); }

            OAuthRequest oauthRequest;
            if (tokenRequest != null && (CableBeachServerState.OAuthCurrentRequests.TryGetValue(tokenRequest.RequestToken, out oauthRequest)))
            {
                // Remove this request from the dictionary of currently tracked requests
                CableBeachServerState.OAuthCurrentRequests.Remove(oauthRequest.Request.RequestToken);

                AuthorizedTokenResponse tokenResponse = CableBeachServerState.OAuthServiceProvider.PrepareAccessTokenMessage(tokenRequest);

                // Get the list of requested capabilities that was sent earlier
                Dictionary<Uri, Uri> capabilities = new Dictionary<Uri, Uri>(oauthRequest.CapabilityNames.Length);
                for (int i = 0; i < oauthRequest.CapabilityNames.Length; i++)
                {
                    Uri serviceIdentifier;
                    if (Uri.TryCreate(oauthRequest.CapabilityNames[i], UriKind.Absolute, out serviceIdentifier))
                        capabilities[serviceIdentifier] = null;
                    else
                        CableBeachServerState.Log.Warn("[CABLE BEACH SERVER]: Unrecognized service identifier in capability request: " + oauthRequest.CapabilityNames[i]);
                }

                // Allow each registered service to attempt to fill in the capabilities request
                CableBeachServerState.CreateCapabilities(httpRequest.Url, oauthRequest.Identity, ref capabilities);

                // Convert the list of capabilities into <string,string> tuples, leaving out requests with empty values
                Dictionary<string, string> capStrings = new Dictionary<string, string>(capabilities.Count);
                foreach (KeyValuePair<Uri, Uri> entry in capabilities)
                {
                    if (entry.Value != null)
                        capStrings[entry.Key.ToString()] = entry.Value.ToString();
                    else
                        CableBeachServerState.Log.Warn("[CABLE BEACH SERVER]: Capability was not created for service identifier " + entry.Key);
                }

                // Put the created capabilities in the OAuth response and send the response
                tokenResponse.SetExtraData(capStrings);
                return OpenAuthHelper.MakeOpenAuthResponse(httpResponse, CableBeachServerState.OAuthServiceProvider.Channel.PrepareResponse(tokenResponse));
            }
            else
            {
                CableBeachServerState.Log.Warn("[CABLE BEACH SERVER]: get_access_token called with invalid or missing request token " + tokenRequest.RequestToken);
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return Utils.EmptyBytes;
            }
        }
    }

    #endregion OAuth Endpoints

    #region OAuth Login/Callbacks

    public class CBOpenIDCallbackHandler : BaseStreamHandler
    {
        public CBOpenIDCallbackHandler(string httpMethod, string path) :
            base(httpMethod, path)
        {
        }

        public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            HttpRequestInfo requestInfo = OpenAuthHelper.GetRequestInfo(httpRequest);

            try
            {
                IAuthenticationResponse openidResponse = CableBeachServerState.OpenIDRelyingParty.GetResponse(requestInfo);

                if (openidResponse != null && openidResponse.Status == AuthenticationStatus.Authenticated)
                {
                    Uri identity = new Uri(openidResponse.ClaimedIdentifier.ToString());
                    string oauthRequestToken = openidResponse.GetCallbackArgument("oauth_request_token");

                    CableBeachServerState.Log.Info("[CABLE BEACH SERVER]: OpenID authentication succeeded for " + identity + ", oauth_request_token=" + oauthRequestToken);

                    OAuthRequest oauthRequest;
                    if (CableBeachServerState.OAuthCurrentRequests.TryGetValue(oauthRequestToken, out oauthRequest))
                    {
                        // OpenID authentication succeeded, store this users claimed identity
                        oauthRequest.Identity = identity;

                        // Ask the user if they want to grant capabilities to the requesting world
                        return CableBeachServerState.BuildPermissionGrantTemplate(oauthRequest);
                    }
                    else
                    {
                        // OpenID auth succeeded but the OAuth token is no longer being tracked
                        CableBeachServerState.Log.Error("[CABLE BEACH SERVER]: Expired or invalid OAuth request token " + oauthRequestToken + " for " + identity);
                        return Encoding.UTF8.GetBytes("OAuth session has expired");
                    }
                }
                else
                {
                    // OpenID authentication was cancelled or had some other failure
                    CableBeachServerState.Log.Error("[CABLE BEACH SERVER]: OpenID authentication failed: " + openidResponse.Status);
                    return Encoding.UTF8.GetBytes("OpenID authentication failed");
                }
            }
            catch (Exception ex)
            {
                CableBeachServerState.Log.Error("[CABLE BEACH SERVER]: Error in OAuth OpenID callback: " + ex.Message, ex);
                return Encoding.UTF8.GetBytes("OpenID authentication failed");
            }
        }
    }

    public class CBPermissionGrantFormHandler : BaseStreamHandler
    {
        public CBPermissionGrantFormHandler(string httpMethod, string path) :
            base(httpMethod, path)
        {
        }

        public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            NameValueCollection query;
            Uri callback;

            try
            {
                byte[] requestData = OpenAuthHelper.GetBody(httpRequest);
                string queryString = HttpUtility.UrlDecode(requestData, Encoding.UTF8);
                query = HttpUtility.ParseQueryString(queryString);
            }
            catch (Exception)
            {
                CableBeachServerState.Log.Error("[CABLE BEACH SERVER]: Failed to parse the permission grant form");
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return Encoding.UTF8.GetBytes("Failed to parse required form parameters");
            }

            if (Uri.TryCreate(OpenAuthHelper.GetQueryValue(query, "callback"), UriKind.Absolute, out callback))
            {
                string confirm = OpenAuthHelper.GetQueryValue(query, "confirm");
                string requestToken = OpenAuthHelper.GetQueryValue(query, "request_token").Replace(' ', '+');

                OAuthRequest oauthRequest;
                if (!String.IsNullOrEmpty(confirm) && !String.IsNullOrEmpty(requestToken))
                {
                    if (CableBeachServerState.OAuthCurrentRequests.TryGetValue(requestToken, out oauthRequest))
                    {
                        // Mark the request token as authorized
                        CableBeachServerState.OAuthTokenManager.AuthorizeRequestToken(requestToken);

                        // Create an authorization response (including a verification code)
                        UserAuthorizationResponse oauthResponse = CableBeachServerState.OAuthServiceProvider.PrepareAuthorizationResponse(oauthRequest.Request);

                        // Update the verification code for this request to the newly created verification code
                        try { CableBeachServerState.OAuthTokenManager.GetRequestToken(requestToken).VerificationCode = oauthResponse.VerificationCode; }
                        catch (KeyNotFoundException)
                        {
                            CableBeachServerState.Log.Warn("[CABLE BEACH SERVER]: Did not recognize request token \"" + requestToken +
                                "\", failed to update verification code");
                        }

                        CableBeachServerState.Log.Warn("[CABLE BEACH SERVER]: OAuth confirmation accepted, redirecting to " + callback);
                        return OpenAuthHelper.MakeOpenAuthResponse(httpResponse, CableBeachServerState.OAuthServiceProvider.Channel.PrepareResponse(oauthResponse));
                    }
                    else
                    {
                        // TODO: We should be redirecting to the callback with a failure parameter set
                        CableBeachServerState.Log.Warn("[CABLE BEACH SERVER]: Could not find an open request matching request token \"" + requestToken + "\"");
                        return Encoding.UTF8.GetBytes("Expired or invalid OAuth session");
                    }
                }
                else
                {
                    // TODO: We should be redirecting to the callback with a failure parameter set
                    CableBeachServerState.Log.Warn("[CABLE BEACH SERVER]: OAuth confirmation (redirecting to " + callback + ") was denied");
                    return Encoding.UTF8.GetBytes("Confirmation denied");
                }
            }
            else
            {
                CableBeachServerState.Log.Error("[CABLE BEACH SERVER]: Received a POST to the OAuth confirmation form with no callback");
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return Encoding.UTF8.GetBytes("No callback specified");
            }
        }
    }

    #endregion OAuth Login/Callbacks
}
