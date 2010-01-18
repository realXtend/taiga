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
using log4net;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenMetaverse;
using OpenMetaverse.Http;
using OpenMetaverse.StructuredData;

namespace OpenSim.Grid.UserServer.Modules
{
    public class FilesystemStreamHandler : IStreamHandler
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly string[] m_forbiddenChars = new[] { "\\", "..", ":" };
        private static readonly string m_pathSeparator = System.IO.Path.DirectorySeparatorChar.ToString();

        private readonly IDictionary<string, string> m_mimeTypes = new Dictionary<string, string>();

        public string ContentType { get { return null; } }
        public string HttpMethod { get { return m_httpMethod; } }
        public string Path { get { return m_path; } }

        string m_httpMethod;
        string m_path;
        string m_filePath;
        UserLoginService m_loginService;

        /// <summary>
        /// Constructor
        /// </summary>
        public FilesystemStreamHandler(string httpMethod, string path, string filePath, UserLoginService loginService)
        {
            m_loginService = loginService;
            m_httpMethod = httpMethod;
            m_path = path;
            m_filePath = filePath;

            m_mimeTypes.Add("default", "application/octet-stream");
            m_mimeTypes.Add("txt", "text/plain");
            m_mimeTypes.Add("html", "text/html");
            m_mimeTypes.Add("htm", "text/html");
            m_mimeTypes.Add("jpg", "image/jpg");
            m_mimeTypes.Add("jpeg", "image/jpg");
            m_mimeTypes.Add("bmp", "image/bmp");
            m_mimeTypes.Add("gif", "image/gif");
            m_mimeTypes.Add("png", "image/png");

            m_mimeTypes.Add("ico", "image/vnd.microsoft.icon");
            m_mimeTypes.Add("css", "text/css");
            m_mimeTypes.Add("gzip", "application/x-gzip");
            m_mimeTypes.Add("zip", "multipart/x-zip");
            m_mimeTypes.Add("tar", "application/x-tar");
            m_mimeTypes.Add("pdf", "application/pdf");
            m_mimeTypes.Add("rtf", "application/rtf");
            m_mimeTypes.Add("xls", "application/vnd.ms-excel");
            m_mimeTypes.Add("ppt", "application/vnd.ms-powerpoint");
            m_mimeTypes.Add("doc", "application/application/msword");
            m_mimeTypes.Add("js", "application/javascript");
            m_mimeTypes.Add("au", "audio/basic");
            m_mimeTypes.Add("snd", "audio/basic");
            m_mimeTypes.Add("es", "audio/echospeech");
            m_mimeTypes.Add("mp3", "audio/mpeg");
            m_mimeTypes.Add("mp2", "audio/mpeg");
            m_mimeTypes.Add("mid", "audio/midi");
            m_mimeTypes.Add("wav", "audio/x-wav");
            m_mimeTypes.Add("swf", "application/x-shockwave-flash");
            m_mimeTypes.Add("avi", "video/avi");
            m_mimeTypes.Add("rm", "audio/x-pn-realaudio");
            m_mimeTypes.Add("ram", "audio/x-pn-realaudio");
            m_mimeTypes.Add("aif", "audio/x-aiff");
        }

        /// <summary>
        /// Handles all GET and POST requests for Facebook Connect logins
        /// </summary>
        public void Handle(string requestPath, Stream request, Stream response, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            if (!CanHandle(httpRequest.Url))
            {
                httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                OpenAuthHelper.AddToBody(httpResponse, "File not found");
                return;
            }

            try
            {
                string path = GetPath(httpRequest.Url);
                string extension = GetFileExtension(path);
                if (extension == null)
                {
                    httpResponse.StatusCode = (int)HttpStatusCode.UnsupportedMediaType;
                    OpenAuthHelper.AddToBody(httpResponse, "Unsupported media type");
                    return;
                }

                if (m_mimeTypes.ContainsKey(extension))
                {
                    httpResponse.ContentType = m_mimeTypes[extension];
                }
                else
                {
                    httpResponse.StatusCode = (int)HttpStatusCode.UnsupportedMediaType;
                    OpenAuthHelper.AddToBody(httpResponse, "Unsupported media type");
                    return;
                }

                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (!String.IsNullOrEmpty(httpRequest.Headers["if-modified-since"]))
                    {
                        DateTime lastRequest = DateTime.Parse(httpRequest.Headers["if-modified-since"]);
                        if (lastRequest.CompareTo(File.GetLastWriteTime(path)) <= 0)
                            httpResponse.StatusCode = (int)HttpStatusCode.NotModified;
                    }

                    httpResponse.AddHeader("Last-modified", File.GetLastWriteTime(path).ToString("r"));
                    httpResponse.ContentLength = stream.Length;

                    if (!httpRequest.HttpMethod.Equals("HEADERS", StringComparison.InvariantCultureIgnoreCase) &&
                        httpResponse.StatusCode != (int)HttpStatusCode.NotModified)
                    {
                        OpenAuthHelper.CopyTo(stream, httpResponse.OutputStream, (int)stream.Length);
                    }
                }
            }
            catch (FileNotFoundException)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                OpenAuthHelper.AddToBody(httpResponse, "File not found");
            }
        }

        /// <summary>
        /// Determines if the request should be handled by this module.
        /// Invoked by the <see cref="HttpServer"/>
        /// </summary>
        /// <param name="uri"></param>
        /// <returns>true if this module should handle it.</returns>
        bool CanHandle(Uri uri)
        {
            if (Contains(uri.AbsolutePath, m_forbiddenChars))
                return false;

            string path = GetPath(uri);
            return
                uri.AbsolutePath.StartsWith(m_path) && // Correct directory
                File.Exists(path) && // File exists
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0; // Not a symlink
        }

        string GetPath(Uri uri)
        {
            if (Contains(uri.AbsolutePath, m_forbiddenChars))
                throw new ArgumentException("Illegal path");

            string path = m_filePath + uri.AbsolutePath.Substring(m_path.Length);
            return path.Replace('/', System.IO.Path.DirectorySeparatorChar);
        }

        static bool Contains(string source, IEnumerable<string> chars)
        {
            foreach (string s in chars)
            {
                if (source.Contains(s))
                    return true;
            }

            return false;
        }

        static string GetFileExtension(string uri)
        {
            int pos = uri.LastIndexOf('.');
            return pos == -1 ? null : uri.Substring(pos + 1);
        }
    }
}
