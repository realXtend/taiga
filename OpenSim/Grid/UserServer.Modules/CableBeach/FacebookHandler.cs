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
using System.Reflection;
using log4net;
using OpenSim.Framework.Servers.HttpServer;
using CableBeachMessages;

namespace OpenSim.Grid.UserServer.Modules
{
    public class FacebookStreamHandler : IStreamHandler
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public string ContentType { get { return null; } }
        public string HttpMethod { get { return m_httpMethod; } }
        public string Path { get { return m_path; } }

        string m_httpMethod;
        string m_path;
        UserLoginService m_loginService;
        FacebookConnect m_fbConnect;

        /// <summary>
        /// Constructor
        /// </summary>
        public FacebookStreamHandler(string httpMethod, string path, UserLoginService loginService)
        {
            m_loginService = loginService;
            m_httpMethod = httpMethod;
            m_path = path;
            m_fbConnect = new FacebookConnect(m_loginService.m_config.FacebookAppKey,
                m_loginService.m_config.FacebookAppSecret);
        }

        /// <summary>
        /// Handles all GET and POST requests for Facebook Connect logins
        /// </summary>
        public void Handle(string path, Stream request, Stream response, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            try
            {
                FacebookConnectSession fbSession = m_fbConnect.GetSession(httpRequest.Cookies);
                Dictionary<string, string> userInfo = FacebookConnect.GetUserInfo(m_fbConnect.ApiKey, m_fbConnect.AppSecret, fbSession.SessionKey, fbSession.UserID);

                if (userInfo != null && userInfo.ContainsKey("profile_url"))
                {
                    string firstName = userInfo["first_name"];
                    string lastName = userInfo["last_name"];
                    string email = userInfo["proxied_email"];
                    Uri identity;
                    Uri.TryCreate(userInfo["profile_url"], UriKind.Absolute, out identity);

                    CableBeachState.StartLogin(httpRequest, httpResponse, identity, firstName, lastName, email, CableBeachAuthMethods.FACEBOOK);
                }
                else
                {
                    m_log.Error("[CABLE BEACH FACEBOOK]: Failed to retrieve user info from the REST API");
                    httpResponse.StatusCode = (int)HttpStatusCode.InternalServerError;
                    CableBeachState.SendLoginTemplate(httpResponse, null, "Connection to Facebook failed");
                }
            }
            catch (FacebookConnectAuthenticationException)
            {
                m_log.Error("[CABLE BEACH FACEBOOK]: No valid Facebook Connect session found");
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                CableBeachState.SendLoginTemplate(httpResponse, null, "No valid Facebook Connect session found");
            }
            catch (Exception ex)
            {
                m_log.Error("[CABLE BEACH FACEBOOK]: HTTP request handling failed: " + ex.Message, ex);
                httpResponse.StatusCode = (int)HttpStatusCode.InternalServerError;
                CableBeachState.SendLoginTemplate(httpResponse, null, ex.Message);
            }
        }
    }
}
