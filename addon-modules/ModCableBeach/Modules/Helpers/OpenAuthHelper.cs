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
using DotNetOpenAuth.OAuth;
using DotNetOpenAuth.OAuth.ChannelElements;
using DotNetOpenAuth.Messaging;
using OpenSim.Framework.Servers.HttpServer;
using OpenMetaverse;

namespace ModCableBeach
{
    public static class OpenAuthHelper
    {
        /*public static ServiceProviderDescription CreateServiceProviderDescription(Service service)
        {
            ServiceProviderDescription desc = new ServiceProviderDescription();
            desc.RequestTokenEndpoint = new MessageReceivingEndpoint(service.OAuthRequestToken, HttpDeliveryMethods.PostRequest);
            desc.UserAuthorizationEndpoint = new MessageReceivingEndpoint(service.OAuthAuthorizeToken, HttpDeliveryMethods.GetRequest);
            desc.AccessTokenEndpoint = new MessageReceivingEndpoint(service.OAuthGetAccessToken, HttpDeliveryMethods.PostRequest);
            desc.TamperProtectionElements = new ITamperProtectionChannelBindingElement[] { new HmacSha1SigningBindingElement() };

            return desc;
        }*/

        public static ServiceProviderDescription CreateServiceProviderDescription(Uri httpBaseUri)
        {
            ServiceProviderDescription desc = new ServiceProviderDescription();
            desc.RequestTokenEndpoint = new MessageReceivingEndpoint(new Uri(httpBaseUri, "/oauth/get_request_token"), HttpDeliveryMethods.PostRequest);
            desc.UserAuthorizationEndpoint = new MessageReceivingEndpoint(new Uri(httpBaseUri, "/oauth/authorize_token"), HttpDeliveryMethods.GetRequest);
            desc.AccessTokenEndpoint = new MessageReceivingEndpoint(new Uri(httpBaseUri, "/oauth/get_access_token"), HttpDeliveryMethods.PostRequest);
            desc.TamperProtectionElements = new ITamperProtectionChannelBindingElement[] { new HmacSha1SigningBindingElement() };

            return desc;
        }

        public static void AddToBody(OSHttpResponse response, string data)
        {
            byte[] bytes = response.ContentEncoding.GetBytes(data);
            response.Body.Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Copies the contents of one stream to another.
        /// </summary>
        /// <param name="copyFrom">The stream to copy from, at the position where copying should begin.</param>
        /// <param name="copyTo">The stream to copy to, at the position where bytes should be written.</param>
        /// <param name="maximumBytesToCopy">The maximum bytes to copy.</param>
        /// <returns>The total number of bytes copied.</returns>
        /// <remarks>
        /// Copying begins at the streams' current positions.
        /// The positions are NOT reset after copying is complete.
        /// </remarks>
        public static int CopyTo(Stream copyFrom, Stream copyTo, int maximumBytesToCopy)
        {
            byte[] buffer = new byte[4096];
            int readBytes;
            int totalCopiedBytes = 0;

            while ((readBytes = copyFrom.Read(buffer, 0, Math.Min(4096, maximumBytesToCopy))) > 0)
            {
                int writeBytes = Math.Min(maximumBytesToCopy, readBytes);
                copyTo.Write(buffer, 0, writeBytes);
                totalCopiedBytes += writeBytes;
                maximumBytesToCopy -= writeBytes;
            }

            return totalCopiedBytes;
        }

        public static string GetStreamString(Stream stream)
        {
            if (stream != null)
            {
                StreamReader reader = new StreamReader(stream);
                string value = reader.ReadToEnd();
                stream.Seek(0, SeekOrigin.Begin);
                return value;
            }

            return null;
        }

        /*public static void OpenAuthResponseToHttp(OSHttpResponse httpResponse, OutgoingWebResponse openAuthResponse)
        {
            httpResponse.StatusCode = (int)openAuthResponse.Status;
            foreach (string key in openAuthResponse.Headers.Keys)
                httpResponse.AddHeader(key, openAuthResponse.Headers[key]);
            if (!String.IsNullOrEmpty(openAuthResponse.Body))
                AddToBody(httpResponse, openAuthResponse.Body);
        }*/

        public static byte[] MakeOpenAuthResponse(OSHttpResponse httpResponse, OutgoingWebResponse openAuthResponse)
        {
            httpResponse.StatusCode = (int)openAuthResponse.Status;
            foreach (string key in openAuthResponse.Headers.Keys)
                httpResponse.AddHeader(key, openAuthResponse.Headers[key]);
            if (!String.IsNullOrEmpty(openAuthResponse.Body))
                return Encoding.UTF8.GetBytes(openAuthResponse.Body);
            else
                return Utils.EmptyBytes;
        }

        public static HttpRequestInfo GetRequestInfo(OSHttpRequest request)
        {
            // Combine HTTP headers and URL query values
            WebHeaderCollection headers = new WebHeaderCollection();
            try { headers.Add(request.Headers); }
            catch (Exception) { }

            string rawUrl = request.Url.AbsolutePath + request.Url.Query + request.Url.Fragment;
            return new HttpRequestInfo(request.HttpMethod, request.Url, rawUrl, headers, request.InputStream);
        }

        public static string CreateQueryString(IDictionary<string, string> args)
        {
            if (args.Count == 0)
            {
                return String.Empty;
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder(args.Count * 10);

            foreach (KeyValuePair<string, string> arg in args)
            {
                sb.Append(HttpUtility.UrlEncode(arg.Key));
                sb.Append('=');
                sb.Append(HttpUtility.UrlEncode(arg.Value));
                sb.Append('&');
            }
            sb.Length--; // remove trailing &

            return sb.ToString();
        }

        public static Dictionary<string, string> QueryStringToDictionary(string queryString)
        {
            NameValueCollection values = HttpUtility.ParseQueryString(queryString);
            Dictionary<string, string> dictionary = new Dictionary<string, string>(values.Count);

            for (int i = 0; i < values.Count; i++)
            {
                string key = values.GetKey(i);
                dictionary[key] = values[key];
            }

            return dictionary;
        }

        public static string GetQueryValue(string query, string key)
        {
            try
            {
                NameValueCollection queryValues = HttpUtility.ParseQueryString(query);
                string[] values = queryValues.GetValues(key);
                if (values != null && values.Length > 0)
                    return values[0];
            }
            catch (Exception) { }

            return null;
        }

        public static string GetQueryValue(NameValueCollection queryValues, string key)
        {
            string[] values = queryValues.GetValues(key);
            if (values != null && values.Length > 0)
                return values[0];
            return null;
        }
    }
}
