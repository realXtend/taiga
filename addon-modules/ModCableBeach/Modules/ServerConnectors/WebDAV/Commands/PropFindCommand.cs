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
using System.Text;
using System.Net;
using System.IO;
using System.Xml;
using System.Xml.XPath;
using OpenSim.Framework.Servers.HttpServer;

namespace ModCableBeach
{
    public class PropFindCommand : ICommand
    {
        WebDAVListener server;

        public void Start(WebDAVListener server, string path)
        {
            this.server = server;
            server.HttpServer.AddStreamHandler(new StreamHandler("PROPFIND", path, PropFindHandler));
        }

        byte[] PropFindHandler(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            string username;
            if (server.AuthenticateRequest(httpRequest, httpResponse, out username))
            {
                //PropertyRequestType requestType = PropertyRequestType.AllProperties;
                List<string> davProperties = new List<string>();
                List<string> validProperties = new List<string>();
                List<string> invalidProperties = new List<string>();

                if (httpRequest.ContentLength != 0)
                {
                    XPathNavigator requestNavigator = new XPathDocument(httpRequest.InputStream).CreateNavigator();
                    XPathNodeIterator propNodeIterator = requestNavigator.SelectDescendants("prop", "DAV:", false);
                    if (propNodeIterator.MoveNext())
                    {
                        XPathNodeIterator nodeChildren = propNodeIterator.Current.SelectChildren(XPathNodeType.All);
                        while (nodeChildren.MoveNext())
                        {
                            XPathNavigator currentNode = nodeChildren.Current;

                            if (currentNode.NodeType == XPathNodeType.Element)
                                davProperties.Add(currentNode.LocalName.ToLower());
                        }
                    }
                }

                using (MemoryStream responseStream = new MemoryStream())
                {
                    using (XmlTextWriter xmlWriter = new XmlTextWriter(responseStream, Encoding.ASCII))
                    {
                        xmlWriter.Formatting = Formatting.Indented;
                        xmlWriter.IndentChar = '\t';
                        xmlWriter.Indentation = 1;
                        xmlWriter.WriteStartDocument();

                        xmlWriter.WriteStartElement("D", "multistatus", "DAV:");
                        // Microsoft cruft (joy)
                        xmlWriter.WriteAttributeString("xmlns:b", "urn:uuid:c2f41010-65b3-11d1-a29f-00aa00c14882");
                        xmlWriter.WriteAttributeString("xmlns:c", "urn:schemas-microsoft-com:office:office");

                        DepthHeader depth = Depth.ParseDepth(httpRequest);

                        IList<IWebDAVResource> resources = server.OnPropFindConnector(username, httpRequest.Url.AbsolutePath, depth);
                        if (resources.Count > 0)
                        {
                            for (int i = 0; i < resources.Count; i++)
                                XmlResponse.WriteResponse(xmlWriter, davProperties, resources[i]);
                        }

                        xmlWriter.WriteEndElement(); // multistatus

                        xmlWriter.WriteEndDocument();
                        xmlWriter.Flush();

                        httpResponse.KeepAlive = httpRequest.KeepAlive;
                        httpResponse.ContentType = "text/xml";
                        httpResponse.ContentEncoding = Encoding.UTF8;
                        httpResponse.AddHeader("DAV", "1");
                        httpResponse.AddHeader("MS-Author-Via", "DAV");
                        httpResponse.AddHeader("Cache-Control", "no-cache");
                        if (resources.Count > 0)
                        {
                            httpResponse.StatusCode = 207; // Multistatus

                            byte[] bytes = responseStream.ToArray();
                            httpResponse.ContentLength = bytes.Length;
                            return bytes;
                        }
                        else
                        {
                            //eventually this should be the same that is defined in HttpListener.Set404Handler()
                            //that however needs some work in HttpServer
                            httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                            httpResponse.StatusDescription = "Not Found";
                            string notFoundResponse = "<html><head><title>Page Not Found</title></head><body><h3>" + httpResponse.StatusDescription + "</h3></body></html>";
                            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(notFoundResponse);
                            httpResponse.ContentLength = buffer.Length;
                            return buffer;
                        }
                    }
                }
            }

            return OpenMetaverse.Utils.EmptyBytes;
        }
    }
}
