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
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Reflection;
using System.Web;
using System.Xml;
using DotNetOpenAuth.OAuth.Messages;
using DotNetOpenAuth.OpenId;
using DotNetOpenAuth.OpenId.Extensions.AttributeExchange;
using DotNetOpenAuth.OpenId.Extensions.SimpleRegistration;
using DotNetOpenAuth.OpenId.Provider;
using DotNetOpenAuth.OpenId.RelyingParty;
using log4net;
using Nwc.XmlRpc;
using OpenSim.Framework;
using OpenSim.Framework.Communications.Services;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Framework.Statistics;
using OpenMetaverse;
using CableBeachMessages;

using OAuthConsumer = DotNetOpenAuth.OAuth.WebConsumer;
using ServiceIdentifier = System.Uri;

namespace OpenSim.Grid.UserServer.Modules
{
    public class OpenIdProviderStreamHandler : IStreamHandler
    {
        // FIXME: Replace this with templates
        #region HTML

        /// <summary>Login form used to authenticate OpenID requests</summary>
        const string LOGIN_PAGE =
@"<html>
<head><title>OpenSim OpenID Login</title></head>
<body>
<h3>OpenSim Login</h3>
<form method=""post"">
<label for=""first"">First Name:</label> <input readonly type=""text"" name=""first"" id=""first"" value=""{0}""/>
<label for=""last"">Last Name:</label> <input readonly type=""text"" name=""last"" id=""last"" value=""{1}""/>
<label for=""pass"">Password:</label> <input type=""password"" name=""pass"" id=""pass""/>
<input type=""submit"" value=""Login"">
</form>
</body>
</html>";

        /// <summary>Page shown for a valid OpenID identity</summary>
        const string OPENID_PAGE =
@"<html>
<head>
<title>{2} {3}</title>
<link rel=""openid2.provider openid.server"" href=""{0}""/>
<link rel=""describedby"" href=""{1}"" type=""application/xrd+xml"" />
</head>
<body>OpenID identifier for {2} {3}</body>
</html>
";

        /// <summary>Page shown if the OpenID endpoint is requested directly</summary>
        const string ENDPOINT_PAGE =
@"<html><head><title>OpenID Endpoint</title></head><body>
This is an OpenID server endpoint, not a human-readable resource.
For more information, see <a href='http://openid.net/'>http://openid.net/</a>.
</body></html>";

        #endregion HTML

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public string ContentType { get { return null; } }
        public string HttpMethod { get { return m_httpMethod; } }
        public string Path { get { return m_path; } }

        string m_httpMethod;
        string m_path;

        /// <summary>
        /// Constructor
        /// </summary>
        public OpenIdProviderStreamHandler(string httpMethod, string path, UserLoginService loginService)
        {
            m_httpMethod = httpMethod;
            m_path = path;
        }

        /// <summary>
        /// Handles all GET and POST requests for OpenID identifier pages and endpoint
        /// server communication
        /// </summary>
        public void Handle(string path, Stream request, Stream response, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            try
            {
                bool isPost = httpRequest.HasEntityBody && httpRequest.Url.AbsolutePath.Contains("/openid/server");

                if (isPost)
                    OpenIDServerPostHandler(httpRequest, httpResponse);
                else
                    OpenIDServerGetHandler(httpRequest, httpResponse);
            }
            catch (Exception ex)
            {
                m_log.Error("[CABLE BEACH IDP]: HTTP request handling failed: " + ex.Message, ex);
                httpResponse.StatusCode = (int)HttpStatusCode.InternalServerError;
                OpenAuthHelper.AddToBody(httpResponse, ex.Message);
            }
        }

        void OpenIDServerGetHandler(OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            // TODO: Get the hostname that we *should* be listening on, and check that against httpRequest.Url.Authority

            if (httpRequest.Url.AbsolutePath.Contains("/openid/server"))
            {
                // Standard HTTP GET was made on the OpenID endpoint, send the client the default error page
                OpenAuthHelper.AddToBody(httpResponse, ENDPOINT_PAGE);
            }
            else
            {
                // Try and lookup this avatar
                UserProfileData profile;
                if (TryGetProfile(httpRequest.Url, out profile))
                {
                    if (httpRequest.Url.AbsolutePath.EndsWith(";xrd"))
                    {
                        Uri identity = new Uri(httpRequest.Url.ToString().Replace(";xrd", String.Empty));

                        // Create an XRD document from the identity URL and filesystem (inventory) service
                        XrdDocument xrd = new XrdDocument(identity.ToString());
                        xrd.Links.Add(new XrdLink(new Uri("http://specs.openid.net/auth"), null, new XrdUri(identity)));
                        xrd.Links.Add(new XrdLink(new Uri(CableBeachServices.FILESYSTEM), "application/json", new XrdUri(CableBeachState.LoginService.m_config.InventoryUrl)));

                        byte[] data = System.Text.Encoding.UTF8.GetBytes(XrdParser.WriteXrd(xrd));
                        httpResponse.ContentLength = data.Length;
                        httpResponse.ContentType = "application/xrd+xml";
                        httpResponse.OutputStream.Write(data, 0, data.Length);
                    }
                    else
                    {
                        // TODO: Print out a full profile page for this avatar
                        Uri openIDServerUri = new Uri(httpRequest.Url, "/openid/server");
                        Uri xrdUri = new Uri(httpRequest.Url, "/users/" + profile.FirstName + "." + profile.SurName + ";xrd");
                        OpenAuthHelper.AddToBody(httpResponse, String.Format(OPENID_PAGE, openIDServerUri, xrdUri, profile.FirstName, profile.SurName));
                    }
                }
                else
                {
                    // Couldn't parse an avatar name, or couldn't find the avatar in the user server
                    httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                    OpenAuthHelper.AddToBody(httpResponse, "OpenID identity not found");
                }
            }
        }

        void OpenIDServerPostHandler(OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            IRequest openidRequest = CableBeachState.Provider.GetRequest(OpenAuthHelper.GetRequestInfo(httpRequest));

            if (openidRequest != null)
            {
                if (openidRequest is DotNetOpenAuth.OpenId.Provider.IAuthenticationRequest)
                {
                    DotNetOpenAuth.OpenId.Provider.IAuthenticationRequest authRequest = (DotNetOpenAuth.OpenId.Provider.IAuthenticationRequest)openidRequest;
                    ClaimsRequest claimsRequest = openidRequest.GetExtension<ClaimsRequest>();

                    // Try and lookup this avatar
                    UserProfileData profile;
                    if (TryGetProfile(httpRequest.Url, out profile))
                    {
                        // Get the password from the POST data
                        NameValueCollection postData = HttpUtility.ParseQueryString(new StreamReader(httpRequest.InputStream).ReadToEnd());
                        string[] passwordValues = postData.GetValues("password");

                        if (passwordValues != null && passwordValues.Length == 1)
                        {
                            if (CableBeachState.LoginService.AuthenticateUser(profile, passwordValues[0]))
                                authRequest.IsAuthenticated = true;
                            else
                                authRequest.IsAuthenticated = false;

                            m_log.Info("[CABLE BEACH IDP]: Password match success for + " + profile.Name + ": " + authRequest.IsAuthenticated);

                            if (authRequest.IsAuthenticated.Value && claimsRequest != null)
                            {
                                // Fill in a few Simple Registration values if there was a request for SREG data
                                ClaimsResponse claimsResponse = claimsRequest.CreateResponse();
                                claimsResponse.Email = profile.Email;
                                claimsResponse.FullName = profile.Name.Trim();
                                claimsResponse.BirthDate = Utils.UnixTimeToDateTime(profile.Created);
                                authRequest.AddResponseExtension(claimsResponse);

                                m_log.Debug("[CABLE BEACH IDP]: Appended SREG values to the positive assertion response");
                            }
                        }
                        else
                        {
                            // Authentication was requested, send the client a login form
                            OpenAuthHelper.AddToBody(httpResponse, String.Format(LOGIN_PAGE, profile.FirstName, profile.SurName));
                            return;
                        }
                    }
                    else
                    {
                        // Cannot find an avatar matching the claimed identifier
                        m_log.Warn("[CABLE BEACH IDP]: (POST) Could not locate an avatar identity from the claimed identitifer " +
                            authRequest.ClaimedIdentifier.ToString());
                        authRequest.IsAuthenticated = false;
                    }
                }

                if (openidRequest.IsResponseReady)
                    OpenAuthHelper.OpenAuthResponseToHttp(httpResponse, CableBeachState.Provider.PrepareResponse(openidRequest));
            }
        }

        /// <summary>
        /// Parse a URL with a relative path of the form /users/First_Last and try to
        /// retrieve the profile matching that avatar name
        /// </summary>
        /// <param name="requestUrl">URL to parse for an avatar name</param>
        /// <param name="profile">Profile data for the avatar</param>
        /// <returns>True if the parse and lookup were successful, otherwise false</returns>
        bool TryGetProfile(Uri requestUrl, out UserProfileData profile)
        {
            if (requestUrl.Segments.Length == 3 && requestUrl.Segments[1] == "users/")
            {
                // Parse the avatar name from the path
                string username = requestUrl.Segments[requestUrl.Segments.Length - 1].Replace(";xrd", String.Empty);
                string[] name = username.Split(new char[] { '_', '.' });

                if (name.Length == 2)
                {
                    profile = CableBeachState.LoginService.GetTheUser(name[0], name[1]);
                    return (profile != null);
                }
            }

            profile = null;
            return false;
        }
    }

    public class OpenIdLoginStreamHandler : IStreamHandler
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public string ContentType { get { return null; } }
        public string HttpMethod { get { return m_httpMethod; } }
        public string Path { get { return m_path; } }

        string m_httpMethod;
        string m_path;

        /// <summary>
        /// Constructor
        /// </summary>
        public OpenIdLoginStreamHandler(string httpMethod, string path, UserLoginService loginService)
        {
            m_httpMethod = httpMethod;
            m_path = path;
        }

        /// <summary>
        /// Handles all GET and POST requests for OpenID logins
        /// </summary>
        public void Handle(string path, Stream request, Stream response, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            try
            {
                UUID sessionID;
                bool isPost = httpRequest.HasEntityBody;

                if (IsXmlRpcLogin(httpRequest.Url, out sessionID))
                    XmlRpcLoginHandler(httpRequest, httpResponse);
                else if (isPost)
                    OpenIDLoginPostHandler(httpRequest, httpResponse);
                else
                    OpenIDLoginGetHandler(httpRequest, httpResponse);
            }
            catch (Exception ex)
            {
                m_log.Error("[CABLE BEACH LOGIN]: HTTP request handling failed: " + ex.Message, ex);
                httpResponse.StatusCode = (int)HttpStatusCode.InternalServerError;
                OpenAuthHelper.AddToBody(httpResponse, ex.Message);
            }
        }

        #region HTTP Handlers

        void OpenIDLoginGetHandler(OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            if (httpRequest.Url.AbsolutePath.EndsWith("openid_callback"))
            {
                #region OpenID Callback

                IAuthenticationResponse authResponse = CableBeachState.RelyingParty.GetResponse(OpenAuthHelper.GetRequestInfo(httpRequest));

                if (authResponse != null)
                {
                    if (authResponse.Status == AuthenticationStatus.Authenticated)
                    {
                        // OpenID authentication succeeded
                        Uri identity = new Uri(authResponse.ClaimedIdentifier.ToString());

                        // Check if this identity is authorized for access. This check is done here for the second time
                        // because the ClaimedIdentifier after authentication has finished is not necessarily the original
                        // OpenID URL entered into the login form
                        if (CableBeachState.IsIdentityAuthorized(identity))
                        {
                            string firstName = null, lastName = null, email = null;

                            // Get the Simple Registration attributes the IDP returned, if any
                            ClaimsResponse sreg = authResponse.GetExtension<ClaimsResponse>();
                            if (sreg != null)
                            {
                                if (!String.IsNullOrEmpty(sreg.FullName))
                                {
                                    string[] firstLast = sreg.FullName.Split(' ');
                                    if (firstLast.Length == 2)
                                    {
                                        firstName = firstLast[0];
                                        lastName = firstLast[1];
                                    }
                                }

                                email = sreg.Email;
                            }

                            CableBeachState.StartLogin(httpRequest, httpResponse, identity, firstName, lastName, email, CableBeachAuthMethods.OPENID);
                        }
                        else
                        {
                            CableBeachState.SendLoginTemplate(httpResponse, null, identity + " is not authorized to access this world");
                        }
                    }
                    else
                    {
                        // Parse an error message out of authResponse
                        string errorMsg = (authResponse.Exception != null) ?
                            authResponse.Exception.Message :
                            authResponse.Status.ToString();

                        CableBeachState.SendLoginTemplate(httpResponse, null, errorMsg);
                    }
                }
                else
                {
                    httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                    OpenAuthHelper.AddToBody(httpResponse, "Invalid or missing OpenID callback data");
                }

                #endregion OpenID Callback
            }
            else if (httpRequest.Url.AbsolutePath.EndsWith("oauth_callback"))
            {
                #region OAuth Callback

                ServiceRequestsData stateData;
                string requestToken = OpenAuthHelper.GetQueryValue(httpRequest.Url.Query, "oauth_token");

                if (!String.IsNullOrEmpty(requestToken) && CableBeachState.CurrentServiceRequests.TryGetValue(requestToken, out stateData))
                {
                    ServiceIdentifier serviceIdentifier = CableBeachState.GetCurrentService(stateData.ServiceRequirements);
                    Service service;
                    CapabilityRequirements capRequirements;

                    if (serviceIdentifier != null)
                    {
                        if (stateData.Services.TryGetValue(serviceIdentifier, out service) &&
                            stateData.ServiceRequirements.TryGetValue(serviceIdentifier, out capRequirements))
                        {
                            try
                            {
                                OAuthConsumer consumer = new OAuthConsumer(OpenAuthHelper.CreateServiceProviderDescription(service), CableBeachState.OAuthTokenManager);
                                AuthorizedTokenResponse tokenResponse = consumer.ProcessUserAuthorization(OpenAuthHelper.GetRequestInfo(httpRequest));

                                // We actually don't need the access token at all since the capabilities should be in this response.
                                // Parse the capabilities out of ExtraData
                                CapabilityRequirements newCaps = new CapabilityRequirements();
                                foreach (KeyValuePair<string, string> capability in tokenResponse.ExtraData)
                                {
                                    Uri capIdentifier, capUri;
                                    if (Uri.TryCreate(capability.Key, UriKind.Absolute, out capIdentifier) &&
                                        Uri.TryCreate(capability.Value, UriKind.Absolute, out capUri))
                                    {
                                        newCaps[capIdentifier] = capUri;
                                    }
                                }

                                m_log.Info("[CABLE BEACH LOGIN]: Fetched " + newCaps.Count + " capabilities through OAuth from " + service.OAuthGetAccessToken);

                                // Update the capabilities for this service
                                stateData.ServiceRequirements[serviceIdentifier] = newCaps;
                            }
                            catch (Exception ex)
                            {
                                m_log.Error("[CABLE BEACH LOGIN]: Failed to exchange request token for capabilities at " + service.OAuthGetAccessToken + ": " + ex.Message);
                                CableBeachState.SendLoginTemplate(httpResponse, null, "OAuth request to " + service.OAuthGetAccessToken + " failed: " + ex.Message);
                                return;
                            }
                        }
                        else
                        {
                            m_log.Error("[CABLE BEACH LOGIN]: OAuth state data corrupted, could not find service or service requirements for " + serviceIdentifier);
                            CableBeachState.SendLoginTemplate(httpResponse, null, "OAuth state data corrupted, please try again");
                            return;
                        }
                    }
                    else
                    {
                        m_log.Warn("[CABLE BEACH LOGIN]: OAuth callback fired but there are no unfulfilled services. Could be a browser refresh");
                    }

                    // Check if we need to continue the cap requesting process
                    CableBeachState.GetCapabilitiesOrCompleteLogin(httpRequest, httpResponse, stateData, requestToken);
                }
                else
                {
                    // A number of different things could lead here (incomplete login sequence, browser refresh of a completed sequence).
                    // Safest thing to do would be to redirect back to the login screen
                    httpResponse.StatusCode = (int)HttpStatusCode.Found;
                    httpResponse.AddHeader("Location", new Uri(httpRequest.Url, "/login/").ToString());
                }

                #endregion OAuth Callback
            }
            else
            {
                // TODO: Get the hostname that we *should* be listening on, and check that against httpRequest.Url.Authority

                // TODO: Check for a client cookie with an authenticated session

                CableBeachState.SendLoginTemplate(httpResponse, null, null);
            }
        }

        void OpenIDLoginPostHandler(OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            byte[] requestData = OpenAuthHelper.GetBody(httpRequest);
            string queryString = HttpUtility.UrlDecode(requestData, System.Text.Encoding.UTF8);
            NameValueCollection query = System.Web.HttpUtility.ParseQueryString(queryString);
            string[] openidIdentifiers = query.GetValues("openid_identifier");

            Uri identity;
            Identifier identifier;

            if (openidIdentifiers != null && openidIdentifiers.Length == 1 &&
                UriIdentifier.TryParse(openidIdentifiers[0], out identifier) &&
                Uri.TryCreate(openidIdentifiers[0], UriKind.Absolute, out identity))
            {
                // Check if this identity is authorized for access
                if (CableBeachState.IsIdentityAuthorized(identity))
                {
                    string baseURL = String.Format("{0}://{1}", httpRequest.Url.Scheme, httpRequest.Url.Authority);
                    Realm realm = new Realm(baseURL);

                    try
                    {
                        DotNetOpenAuth.OpenId.RelyingParty.IAuthenticationRequest authRequest =
                            CableBeachState.RelyingParty.CreateRequest(identifier, realm, new Uri(httpRequest.Url, "/login/openid_callback"));

                        // Add a Simple Registration request to the OpenID request
                        ClaimsRequest sreg = new ClaimsRequest();
                        sreg.BirthDate = DemandLevel.Request;
                        sreg.Email = DemandLevel.Request;
                        sreg.FullName = DemandLevel.Request;
                        sreg.Gender = DemandLevel.Request;
                        sreg.Language = DemandLevel.Request;
                        sreg.Nickname = DemandLevel.Request;
                        sreg.TimeZone = DemandLevel.Request;
                        authRequest.AddExtension(sreg);

                        // Add an Attribute Exchange request to the OpenID request
                        FetchRequest ax = new FetchRequest();
                        ax.Attributes.AddOptional(AvatarAttributes.BIOGRAPHY.ToString());
                        ax.Attributes.AddOptional(AvatarAttributes.BIRTH_DATE.ToString());
                        ax.Attributes.AddOptional(AvatarAttributes.COMPANY.ToString());
                        ax.Attributes.AddOptional(AvatarAttributes.EMAIL.ToString());
                        ax.Attributes.AddOptional(AvatarAttributes.FIRST_NAME.ToString());
                        ax.Attributes.AddOptional(AvatarAttributes.LANGUAGE.ToString());
                        ax.Attributes.AddOptional(AvatarAttributes.LAST_NAME.ToString());
                        ax.Attributes.AddOptional(AvatarAttributes.TIMEZONE.ToString());
                        ax.Attributes.AddOptional(AvatarAttributes.WEBSITE.ToString());
                        authRequest.AddExtension(ax);

                        OpenAuthHelper.OpenAuthResponseToHttp(httpResponse, authRequest.RedirectingResponse);
                    }
                    catch (Exception ex)
                    {
                        CableBeachState.SendLoginTemplate(httpResponse, null, "OpenID login failed: " + ex.Message);
                    }
                }
                else
                {
                    CableBeachState.SendLoginTemplate(httpResponse, null, identity + " is not authorized to access this world");
                }
            }
            else
            {
                CableBeachState.SendLoginTemplate(httpResponse, null, "Please fill in the OpenID URL field");
            }
        }

        /// <summary>
        /// We can't bind XML-RPC handlers to specific paths with the OpenSim
        /// HTTP server, so this method works around that fact
        /// </summary>
        void XmlRpcLoginHandler(OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            XmlRpcRequest xmlRpcRequest = null;
            XmlRpcResponse xmlRpcResponse = new XmlRpcResponse();

            try
            {
                using (TextReader requestReader = new StreamReader(httpRequest.InputStream))
                    xmlRpcRequest = CableBeachState.XmlRpcLoginDeserializer.Deserialize(requestReader) as XmlRpcRequest;
            }
            catch (XmlException) { }

            if (xmlRpcRequest != null)
            {
                string methodName = xmlRpcRequest.MethodName;
                m_log.Info("[CABLE BEACH XMLRPC]: Received an incoming XML-RPC request: " + methodName);

                if (methodName != null && methodName.Equals("login_to_simulator", StringComparison.InvariantCultureIgnoreCase))
                {
                    xmlRpcRequest.Params.Add(httpRequest.RemoteIPEndPoint); // Param[1]

                    try
                    {
                        xmlRpcResponse = LoginHandler(xmlRpcRequest, httpRequest.Url);
                    }
                    catch (Exception e)
                    {
                        // Code set in accordance with http://xmlrpc-epi.sourceforge.net/specs/rfc.fault_codes.php
                        xmlRpcResponse.SetFault(-32603, String.Format("Requested method [{0}] threw exception: {1}",
                            methodName, e));
                    }
                }
            }
            else
            {
                m_log.Warn("[CABLE BEACH XMLRPC]: Received a login request with an invalid or missing XML-RPC body");
            }

            #region Send the Response

            httpResponse.ContentType = "text/xml";
            httpResponse.SendChunked = false;
            httpResponse.ContentEncoding = System.Text.Encoding.UTF8;

            try
            {
                MemoryStream memoryStream = new MemoryStream();
                using (XmlTextWriter writer = new XmlTextWriter(memoryStream, System.Text.Encoding.UTF8))
                {
                    XmlRpcResponseSerializer.Singleton.Serialize(writer, xmlRpcResponse);
                    writer.Flush();

                    httpResponse.ContentLength = memoryStream.Length;
                    httpResponse.OutputStream.Write(memoryStream.GetBuffer(), 0, (int)memoryStream.Length);
                }
            }
            catch (Exception ex)
            {
                m_log.Warn("[CABLE BEACH XMLRPC]: Error writing to the response stream: " + ex.Message);
            }

            #endregion Send the Response
        }

        XmlRpcResponse LoginHandler(XmlRpcRequest request, Uri requestUrl)
        {
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable requestData = (Hashtable)request.Params[0];
            IPEndPoint remoteClient = null;
            if (request.Params.Count > 1)
                remoteClient = request.Params[1] as IPEndPoint;

            UserProfileData userProfile;
            LoginResponse logResponse = new LoginResponse();

            UUID sessionID;
            IsXmlRpcLogin(requestUrl, out sessionID);
            m_log.Info("[CABLE BEACH XMLRPC]: XML-RPC Received login request message with sessionID " + sessionID);

            string startLocationRequest = "last";
            if (requestData.Contains("start"))
                startLocationRequest = (requestData["start"] as string) ?? "last";

            string clientVersion = "Unknown";
            if (requestData.Contains("version"))
                clientVersion = (requestData["version"] as string) ?? "Unknown";

            if (TryAuthenticateXmlRpcLogin(sessionID, out userProfile))
            {
                try
                {
                    UUID agentID = userProfile.ID;
                    LoginService.InventoryData skeleton = null;

                    try { skeleton = CableBeachState.LoginService.GetInventorySkeleton(agentID); }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat("[CABLE BEACH XMLRPC]: Error retrieving inventory skeleton of agent {0} - {1}",
                            agentID, e);

                        // Let's not panic
                        if (!CableBeachState.LoginService.AllowLoginWithoutInventory())
                            return logResponse.CreateLoginInventoryFailedResponse();
                    }

                    #region Inventory Skeletons

                    if (skeleton != null)
                    {
                        ArrayList AgentInventoryArray = skeleton.InventoryArray;

                        Hashtable InventoryRootHash = new Hashtable();
                        InventoryRootHash["folder_id"] = skeleton.RootFolderID.ToString();
                        ArrayList InventoryRoot = new ArrayList();
                        InventoryRoot.Add(InventoryRootHash);
                        userProfile.RootInventoryFolderID = skeleton.RootFolderID;

                        logResponse.InventoryRoot = InventoryRoot;
                        logResponse.InventorySkeleton = AgentInventoryArray;
                    }

                    // Inventory Library Section
                    Hashtable InventoryLibRootHash = new Hashtable();
                    InventoryLibRootHash["folder_id"] = "00000112-000f-0000-0000-000100bba000";
                    ArrayList InventoryLibRoot = new ArrayList();
                    InventoryLibRoot.Add(InventoryLibRootHash);

                    logResponse.InventoryLibRoot = InventoryLibRoot;
                    logResponse.InventoryLibraryOwner = CableBeachState.LoginService.GetLibraryOwner();
                    logResponse.InventoryLibrary = CableBeachState.LoginService.GetInventoryLibrary();

                    logResponse.CircuitCode = Util.RandomClass.Next();
                    logResponse.Lastname = userProfile.SurName;
                    logResponse.Firstname = userProfile.FirstName;
                    logResponse.AgentID = agentID;
                    logResponse.SessionID = userProfile.CurrentAgent.SessionID;
                    logResponse.SecureSessionID = userProfile.CurrentAgent.SecureSessionID;
                    logResponse.Message = CableBeachState.LoginService.GetMessage();
                    logResponse.BuddList = CableBeachState.LoginService.ConvertFriendListItem(CableBeachState.LoginService.UserManager.GetUserFriendList(agentID));
                    logResponse.StartLocation = startLocationRequest;

                    #endregion Inventory Skeletons

                    if (CableBeachState.LoginService.CustomiseResponse(logResponse, userProfile, startLocationRequest, remoteClient))
                    {
                        userProfile.LastLogin = userProfile.CurrentAgent.LoginTime;
                        CableBeachState.LoginService.CommitAgent(ref userProfile);

                        // If we reach this point, then the login has successfully logged onto the grid
                        if (StatsManager.UserStats != null)
                            StatsManager.UserStats.AddSuccessfulLogin();

                        m_log.DebugFormat("[CABLE BEACH XMLRPC]: Authentication of user {0} {1} successful. Sending response to client",
                            userProfile.FirstName, userProfile.FirstName);

                        return logResponse.ToXmlRpcResponse();
                    }
                    else
                    {
                        m_log.ErrorFormat("[CABLE BEACH XMLRPC]: Informing user {0} {1} that login failed due to an unavailable region",
                            userProfile.FirstName, userProfile.FirstName);

                        return logResponse.CreateDeadRegionResponse();
                    }
                }
                catch (Exception e)
                {
                    m_log.Error("[CABLE BEACH XMLRPC]: Login failed, returning a blank response. Error: " + e);
                    return response;
                }
            }
            else
            {
                m_log.Warn("[CABLE BEACH XMLRPC]: Authentication failed using sessionID " + sessionID + ", there are " +
                    CableBeachState.PendingLogins.Count + " valid pending logins");
                return logResponse.CreateLoginFailedResponse();
            }
        }

        #endregion HTTP Handlers

        bool IsXmlRpcLogin(Uri requestUrl, out UUID sessionID)
        {
            for (int i = requestUrl.Segments.Length - 1; i >= 0; i--)
            {
                if (UUID.TryParse(requestUrl.Segments[i].Replace("/", String.Empty), out sessionID))
                    return true;
            }

            sessionID = UUID.Zero;
            return false;
        }

        bool TryAuthenticateXmlRpcLogin(UUID sessionID, out UserProfileData userProfile)
        {
            // No need to delete the sessionID from pendinglogins, it will expire eventually
            if (CableBeachState.PendingLogins.TryGetValue(sessionID, out userProfile))
                return true;

            userProfile = null;
            return false;
        }
    }
}
