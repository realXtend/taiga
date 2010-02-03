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
using System.Globalization;
using System.Net;
using System.Text;
using System.Xml;

namespace ModCableBeach
{
    public static class WebDAVUtils
    {
        public static void WriteProperty(XmlTextWriter xmlWriter, IWebDAVResource resource, string property)
        {
            switch (property)
            {
                case "ishidden":
                    xmlWriter.WriteStartElement(property, "DAV:");
                    xmlWriter.WriteAttributeString("b:dt", "boolean");
                    xmlWriter.WriteString(resource.IsHidden ? "1" : "0");
                    xmlWriter.WriteEndElement();
                    break;
                case "isreadonly":
                    xmlWriter.WriteStartElement(property, "DAV:");
                    xmlWriter.WriteAttributeString("b:dt", "boolean");
                    xmlWriter.WriteString(resource.IsReadOnly ? "1" : "0");
                    xmlWriter.WriteEndElement();
                    break;
                case "iscollection":
                    xmlWriter.WriteStartElement(property, "DAV:");
                    xmlWriter.WriteAttributeString("b:dt", "boolean");
                    xmlWriter.WriteString(resource.IsFolder ? "1" : "0");
                    xmlWriter.WriteEndElement();
                    break;
                case "getcontenttype":
                    xmlWriter.WriteElementString(property, "DAV:", resource.ContentType);
                    break;
                case "getcontentlanguage":
                    xmlWriter.WriteElementString(property, "DAV:", "en-us");
                    break;
                case "creationdate":
                    xmlWriter.WriteStartElement(property, "DAV:");
                    xmlWriter.WriteAttributeString("b:dt", "dateTime.tz"); //fun, fun MS stuff
                    xmlWriter.WriteString(resource.CreationDate.ToUniversalTime().ToString("s", CultureInfo.InvariantCulture)+"Z");
                    xmlWriter.WriteEndElement();
                    break;
                case "lastaccessed":
                    xmlWriter.WriteStartElement(property, "DAV:");
                    xmlWriter.WriteAttributeString("b:dt", "dateTime.tz");
                    // Microsoft cruft (joy)
                    xmlWriter.WriteString(resource.LastAccessedDate.ToUniversalTime().ToString("s", CultureInfo.InvariantCulture) + "Z");
                    //xmlWriter.WriteString(resource.LastAccessedDate.ToUniversalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture));
                    xmlWriter.WriteEndElement();
                    break;
                case "getlastmodified":
                    xmlWriter.WriteStartElement(property, "DAV:");
                    //Only 'rfc1123-date' productions are legal as values
                    //see: http://tools.ietf.org/html/rfc4918#section-15.7
                    xmlWriter.WriteAttributeString("b:dt", "dateTime.rfc1123"); 
                    xmlWriter.WriteString(resource.LastModifiedDate.ToUniversalTime().ToString("r", CultureInfo.InvariantCulture));
                    xmlWriter.WriteEndElement();
                    break;
                case "getcontentlength":
                    xmlWriter.WriteElementString(property, "DAV:", resource.ContentLength.ToString());
                    break;
                case "displayname":
                    xmlWriter.WriteElementString(property, "DAV:", GetDisplayName(resource.Path));
                    break;
                case "filepath":
                    xmlWriter.WriteElementString(property, "DAV:", resource.Path);
                    break;
                case "resourcetype":
                    if (resource.IsFolder)
                    {
                        xmlWriter.WriteStartElement(property, "DAV:");
                        xmlWriter.WriteElementString("collection", "DAV:", String.Empty);
                        xmlWriter.WriteEndElement();
                    }
                    else
                    {
                        xmlWriter.WriteElementString(property, "DAV:", String.Empty);
                    }
                    break;
                case "name":
                case "parentname":
                case "isstructureddocument":
                case "defaultdocument":
                case "isroot":
                case "contentclass":
                default:
                    xmlWriter.WriteElementString(property, "DAV:", String.Empty);
                    break;
            }
        }

        public static string GetHttpStatusString(HttpStatusCode statusCode)
        {
            string response = "HTTP/1.1 " + (int)statusCode;

            switch (statusCode)
            {
                case HttpStatusCode.Conflict:
                    response += " Conflict";
                    break;
                case HttpStatusCode.Forbidden:
                    response += " Forbidden";
                    break;
                case HttpStatusCode.OK:
                    response += " OK";
                    break;
                case HttpStatusCode.NotFound:
                    response += " Not Found";
                    break;
                case (HttpStatusCode)423:
                    response += " Locked";
                    break;
                case (HttpStatusCode)424:
                    response += " Failed Dependency";
                    break;
                case (HttpStatusCode)507:
                    response += " Insufficient Storage";
                    break;
            }

            return response;
        }

        public static string GetDisplayName(string path)
        {
            path = path.Trim();

            if (path.EndsWith("/"))
                path = path.Substring(0, path.Length - 1);

            if (!path.Contains("/"))
                return path;

            string[] pathParts = path.Split('/');
            return pathParts[pathParts.Length - 1];

        }
    }
}
