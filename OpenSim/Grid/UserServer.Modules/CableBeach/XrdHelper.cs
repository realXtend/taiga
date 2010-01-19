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
using System.Text.RegularExpressions;
using System.Web;
using System.Web.UI.HtmlControls;
using System.Xml;
using log4net;
using CableBeachMessages;

namespace OpenSim.Grid.UserServer.Modules
{
    public static class XrdHelper
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static Service CreateServiceFromLRDD(Uri serviceLocation, Uri serviceType, bool allowOverride)
        {
            HttpWebResponse response;
            Uri xrdUrl = null;
            MemoryStream xrdStream = null;

            MemoryStream serviceStream = FetchWebDocument(serviceLocation,
                "text/html,application/xhtml+xml,application/xrd+xml,application/xml,text/xml", out response);

            if (serviceStream != null)
            {
                if (IsXrdDocument(response.ContentType.ToLowerInvariant(), serviceStream))
                {
                    // We fetched an XRD document directly, skip ahead
                    xrdUrl = serviceLocation;
                    xrdStream = serviceStream;
                }
                else
                {
                    #region LRDD

                    // 1. Check the HTTP headers for Link: <...>; rel="describedby"; ...
                    xrdUrl = FindXrdDocumentLocationInHeaders(response.Headers);

                    // 2. Check the document body for <link rel="describedby" ...>
                    if (xrdUrl == null)
                        xrdUrl = FindXrdDocumentLocationInHtmlMetaTags(OpenAuthHelper.GetStreamString(serviceStream));

                    // 3. TODO: Try and grab the /host-meta document
                    if (xrdUrl == null)
                        xrdUrl = FindXrdDocumentLocationFromHostMeta(new Uri(serviceLocation, "/host-meta"));

                    // 4. Fetch the XRD document
                    if (xrdUrl != null)
                    {
                        serviceStream = FetchWebDocument(xrdUrl, "application/xrd+xml,application/xml,text/xml", out response);

                        if (serviceStream != null && IsXrdDocument(response.ContentType.ToLowerInvariant(), serviceStream))
                            xrdStream = serviceStream;
                        else
                            m_log.Error("[CABLE BEACH XRD]: XRD fetch from " + xrdUrl + " failed");

                        response.Close();
                    }

                    #endregion LRDD
                }

                response.Close();

                if (xrdStream != null)
                    return XrdDocumentToService(xrdStream, xrdUrl, serviceType, allowOverride);
            }
            else
            {
                m_log.Error("[CABLE BEACH XRD]: Discovery on endpoint " + serviceLocation + " failed");
            }

            return null;
        }

        public static MemoryStream FetchWebDocument(Uri location, string acceptTypes, out HttpWebResponse response)
        {
            const int MAXIMUM_BYTES = 1024 * 1024;
            const int TIMEOUT = 10000;
            const int READ_WRITE_TIMEOUT = 1500;
            const int MAXIMUM_REDIRECTS = 10;

            try
            {
                HttpWebRequest request = UntrustedHttpWebRequest.Create(location, true, READ_WRITE_TIMEOUT, TIMEOUT, MAXIMUM_REDIRECTS);
                request.Accept = acceptTypes;

                response = (HttpWebResponse)request.GetResponse();
                MemoryStream documentStream;

                using (Stream networkStream = response.GetResponseStream())
                {
                    documentStream = new MemoryStream(response.ContentLength < 0 ? 4 * 1024 : Math.Min((int)response.ContentLength, MAXIMUM_BYTES));
                    OpenAuthHelper.CopyTo(networkStream, documentStream, MAXIMUM_BYTES);
                    documentStream.Seek(0, SeekOrigin.Begin);
                }

                if (response.StatusCode == HttpStatusCode.OK)
                    return documentStream;
                else
                    m_log.ErrorFormat("[CABLE BEACH XRD]: HTTP error code {0} returned while fetching {1}", response.StatusCode, location);
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[CABLE BEACH XRD]: HTTP error while fetching {0}: {1}", location, ex.Message);
            }

            response = null;
            return null;
        }

        public static bool IsXrdDocument(string contentType, Stream documentStream)
        {
            if (String.IsNullOrEmpty(contentType))
                return false;

            if (contentType == "application/xrd+xml")
                return true;

            if (contentType.EndsWith("xml"))
            {
                documentStream.Seek(0, SeekOrigin.Begin);
                XmlReader reader = XmlReader.Create(documentStream);
                while (reader.Read() && reader.NodeType != XmlNodeType.Element)
                {
                    // Skip over non-element nodes
                }

                if (reader.Name == "XRD")
                    return true;
            }

            return false;
        }

        static Uri FindXrdDocumentLocationInHtmlMetaTags(string html)
        {
            foreach (HtmlLink linkTag in HtmlHeadParser.HeadTags<HtmlLink>(html))
            {
                string rel = linkTag.Attributes["rel"];
                if (rel != null && rel.Equals("describedby", StringComparison.OrdinalIgnoreCase))
                {
                    Uri uri;
                    if (Uri.TryCreate(linkTag.Href, UriKind.Absolute, out uri))
                        return uri;
                }
            }

            return null;
        }

        static Uri FindXrdDocumentLocationInHeaders(WebHeaderCollection headers)
        {
            Uri xrdUrl = null;

            string[] links = headers.GetValues("link");
            if (links != null && links.Length > 0)
            {
                for (int i = 0; i < links.Length; i++)
                {
                    string link = links[i];
                    if (link.Contains("rel=\"describedby\""))
                    {
                        if (Uri.TryCreate(Regex.Replace(link, @"^.*<(.*?)>.*$", "$1"), UriKind.Absolute, out xrdUrl))
                            break;
                    }
                }
            }

            return xrdUrl;
        }

        static Uri FindXrdDocumentLocationFromHostMeta(Uri hostMetaLocation)
        {
            // TODO: Implement this
            return null;
        }

        static Service XrdDocumentToService(Stream xriStream, Uri xrdLocation, Uri serviceType, bool allowOverride)
        {
            XrdParser parser = new XrdParser(xriStream);
            XrdDocument xrd = parser.Document;

            Uri SEED_CAPABILITY = new Uri(CableBeachServices.SEED_CAPABILITY);
            Uri OAUTH_INITIATE = new Uri(CableBeachServices.OAUTH_INITIATE);
            Uri OAUTH_AUTHORIZE = new Uri(CableBeachServices.OAUTH_AUTHORIZE);
            Uri OAUTH_TOKEN = new Uri(CableBeachServices.OAUTH_TOKEN);

            Uri seedCap = null, oauthRequest = null, oauthAuthorize = null, oauthAccess = null;

            // Grab the endpoints we are looking for from the XRD links
            for (int i = 0; i < xrd.Links.Count; i++)
            {
                XrdLink link = xrd.Links[i];

                if (link.Relation == SEED_CAPABILITY)
                    seedCap = GetHighestPriorityUri(link.Uris);
                else if (link.Relation == OAUTH_INITIATE)
                    oauthRequest = GetHighestPriorityUri(link.Uris);
                else if (link.Relation == OAUTH_AUTHORIZE)
                    oauthAuthorize = GetHighestPriorityUri(link.Uris);
                else if (link.Relation == OAUTH_TOKEN)
                    oauthAccess = GetHighestPriorityUri(link.Uris);
            }

            // Check that this service actually fulfills the type of service we need
            bool serviceMatch = false;
            for (int i = 0; i < xrd.Types.Count; i++)
            {
                if (serviceType.ToString() == xrd.Types[i])
                {
                    serviceMatch = true;
                    break;
                }
            }
            if (!serviceMatch)
            {
                m_log.Error("[CABLE BEACH XRD]: Discovery failed at endpoint " + xrdLocation + ", does not provide the service type " + serviceType);
                return null;
            }

            // Check that either a seed cap or all of the OAuth endpoints were fetched
            bool hasSeedCap = (seedCap != null);
            bool hasOAuth = (oauthRequest != null && oauthAuthorize != null && oauthAccess != null);
            if (!hasSeedCap && !hasOAuth)
            {
                m_log.Error("[CABLE BEACH XRD]: Discovery failed at endpoint " + xrdLocation + ", incomplete list of required service endpoints");
                return null;
            }

            // Success, return the service
            return new Service(
                xrdLocation,
                seedCap,
                oauthRequest,
                oauthAuthorize,
                oauthAccess,
                allowOverride);
        }

        static Uri GetHighestPriorityUri(List<XrdUri> uris)
        {
            Uri topUri = null;
            int topPriority = Int32.MaxValue;

            for (int i = 0; i < uris.Count; i++)
            {
                XrdUri uri = uris[i];

                // If this is the highest priority URI, set the top URI to this URI
                if (uri.Priority >= 0 && uri.Priority < topPriority)
                {
                    topUri = uri.Uri;
                    topPriority = uri.Priority;
                }

                // If this URI has no priority and no top URI has been set yet, set the top URI to this URI
                if (topUri == null && uri.Priority < 0)
                    topUri = uri.Uri;
            }

            return topUri;
        }
    }
}
