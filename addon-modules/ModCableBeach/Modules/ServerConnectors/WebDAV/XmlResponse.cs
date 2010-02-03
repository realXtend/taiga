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
using System.Text;
using System.Xml;
using System.IO;

namespace ModCableBeach
{
    public class XmlResponse
    {
        static readonly string[] FILE_ALLOWED_PROPERTIES = new string[]
        {
            "name",
            "parentname",
            "href",
            "ishidden",
            "isreadonly",
            "getcontenttype",
            "contentclass",
            "getcontentlanguage",
            "creationdate",
            "lastaccessed",
            "getlastmodified",
            "getcontentlength",
            "iscollection",
            "isstructureddocument",
            "defaultdocument",
            "displayname",
            "filepath",
            "isroot",
            "resourcetype",
        };

        static readonly string[] FOLDER_ALLOWED_PROPERTIES = new string[]
        {
            "getcontentlanguage",
            "getcontentlength",
            "getcontenttype",
            "creationdate",
            "displayname",
            "filepath",
            "ishidden",
            "isreadonly",
            "iscollection",
            "lastaccessed",
            "getlastmodified",
            "resourcetype",
            "supportedlock",
        };

        static readonly Dictionary<string, string> fileAllowedProperties;
        static readonly Dictionary<string, string> folderAllowedProperties;

        static XmlResponse()
        {
            fileAllowedProperties = new Dictionary<string, string>(FILE_ALLOWED_PROPERTIES.Length);
            for (int i = 0; i < FILE_ALLOWED_PROPERTIES.Length; i++)
                fileAllowedProperties.Add(FILE_ALLOWED_PROPERTIES[i], FILE_ALLOWED_PROPERTIES[i]);

            folderAllowedProperties = new Dictionary<string, string>(FOLDER_ALLOWED_PROPERTIES.Length);
            for (int i = 0; i < FOLDER_ALLOWED_PROPERTIES.Length; i++)
                folderAllowedProperties.Add(FOLDER_ALLOWED_PROPERTIES[i], FOLDER_ALLOWED_PROPERTIES[i]);
        }

        public static byte[] WriteMultiStatusResponseBody(Dictionary<string, HttpStatusCode> multiStatus)
        {
            byte[] bytes;
            //Add status information XML and add that XML to body
            using (MemoryStream responseStream = new MemoryStream())
            {
                XmlTextWriter xmlWriter = new XmlTextWriter(responseStream, Encoding.ASCII); //, Encoding.UTF8);

                xmlWriter.Formatting = Formatting.Indented;
                xmlWriter.IndentChar = '\t';
                xmlWriter.Indentation = 1;
                xmlWriter.WriteStartDocument();
                xmlWriter.WriteStartElement("D", "multistatus", "DAV:");

                foreach (KeyValuePair<string, HttpStatusCode> kvp in multiStatus)
                {
                    xmlWriter.WriteStartElement("response", "DAV:");
                    xmlWriter.WriteElementString("href", "DAV:", kvp.Key);
                    xmlWriter.WriteElementString("status", "DAV:", WebDAVUtils.GetHttpStatusString(kvp.Value));
                    xmlWriter.WriteEndElement(); //response
                }

                xmlWriter.WriteEndElement(); // multistatus
                xmlWriter.WriteEndDocument();
                xmlWriter.Flush();

                bytes = responseStream.ToArray();
            }
            return bytes;
        }

        public static void WriteResponse(XmlTextWriter xmlWriter, List<string> davProperties, IWebDAVResource resource)
        {
            List<string> validProperties = new List<string>();
            List<string> invalidProperties = new List<string>();

            xmlWriter.WriteStartElement("response", "DAV:");
            xmlWriter.WriteElementString("href", "DAV:", resource.Path);

            xmlWriter.WriteStartElement("propstat", "DAV:");
            xmlWriter.WriteElementString("status", "DAV:", WebDAVUtils.GetHttpStatusString(HttpStatusCode.OK));

            xmlWriter.WriteStartElement("prop", "DAV:");

            // Not specifying any properties is a request for all properties
            if (davProperties.Count == 0)
            {
                if (resource.IsFolder)
                    validProperties = new List<string>(FOLDER_ALLOWED_PROPERTIES);
                else
                    validProperties = new List<string>(FILE_ALLOWED_PROPERTIES);
            }
            else
            {
                Dictionary<string, string> allowedProperties = 
                    resource.IsFolder ? folderAllowedProperties : fileAllowedProperties;

                for (int i = 0; i < davProperties.Count; i++)
                {
                    if (allowedProperties.ContainsKey(davProperties[i]))
                        validProperties.Add(davProperties[i]);
                    else
                        invalidProperties.Add(davProperties[i]);
                }
            }

            for (int i = 0; i < validProperties.Count; i++)
                WebDAVUtils.WriteProperty(xmlWriter, resource, validProperties[i]);

            xmlWriter.WriteEndElement(); // prop
            xmlWriter.WriteEndElement(); // propstat

            if (invalidProperties.Count > 0)
            {
                xmlWriter.WriteStartElement("propstat", "DAV:");
                xmlWriter.WriteElementString("status", "DAV:", WebDAVUtils.GetHttpStatusString(HttpStatusCode.NotFound));

                xmlWriter.WriteStartElement("prop", "DAV:");

                foreach (string invalidProp in invalidProperties)
                    xmlWriter.WriteElementString(invalidProp, "DAV:", String.Empty);

                xmlWriter.WriteEndElement(); // prop
                xmlWriter.WriteEndElement(); // propstat
            }

            xmlWriter.WriteEndElement(); // response
        }
    }
}
