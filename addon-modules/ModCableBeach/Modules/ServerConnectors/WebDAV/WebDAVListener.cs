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
using System.IO;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Web;
using System.Xml;
using System.Xml.XPath;
using OpenSim.Framework.Servers.HttpServer;

namespace ModCableBeach
{
    public enum PasswordFormat
    {
        Plain = 0,
        MD5
    }

    public enum AuthenticationType
    {
        None = 0,
        Basic,
        Digest
    }

    public enum PropertyRequestType
    {
        /// <summary>
        /// No properties requested
        /// </summary>
        None = 0,
        /// <summary>
        /// Specific properties requested
        /// </summary>
        NamedProperties,
        /// <summary>
        /// A summary of all the available properties
        /// </summary>
        PropertyNames,
        /// <summary>
        /// All properties requested
        /// </summary>
        AllProperties
    }

    public delegate bool BasicAuthenticationCallback(string username, string password);
    public delegate bool DigestAuthenticationCallback(string username, out PasswordFormat format, out string password);
    public delegate IList<IWebDAVResource> PropFindCallback(string username, string path, DepthHeader depth);
    public delegate WebDAVLockResponse LockCallback(WebDAVLockRequest request);
    public delegate HttpStatusCode UnlockCallback(string path, string locktoken, string username);
    public delegate HttpStatusCode MkcolCallback(string path, string username, string[] ifHeaders);
    public delegate HttpStatusCode MoveCopyCallback(string username, Uri uri, string destination, DepthHeader depth, bool overwrite, string[] ifHeaders, out Dictionary<String, HttpStatusCode> multiStatusValues);
    public delegate HttpStatusCode GetCallback(OSHttpResponse response, string path, string username);
    public delegate HttpStatusCode PutCallback(OSHttpRequest request, string path, string username, string[] ifHeaders);
    public delegate HttpStatusCode DeleteCallback(Uri uri, string username, out Dictionary<string, HttpStatusCode> multiStatus, string[] ifHeaders);
    public delegate HttpStatusCode PropPatchCallback(string username, Uri uri, string uriPath, string nspace, Dictionary<string, string> setProperties, List<string> removeProperties, out Dictionary<string, HttpStatusCode> multiStatus, string[] ifHeaders);

    public class WebDAVListener
    {
        public event BasicAuthenticationCallback OnBasicAuthenticate;
        public event DigestAuthenticationCallback OnDigestAuthenticate;
        public event PropFindCallback OnPropFind;
        public event LockCallback OnLock;
        public event UnlockCallback OnUnlock;
        public event MkcolCallback OnNewCol;
        public event MoveCopyCallback OnMove;
        public event GetCallback OnGet;
        public event PutCallback OnPut;
        public event DeleteCallback OnDelete;
        public event MoveCopyCallback OnCopy;
        public event PropPatchCallback OnPropPatch;

        public AuthenticationType Authentication;
        public IHttpServer HttpServer;

        private List<ICommand> commands = new List<ICommand>();

        public WebDAVListener(IHttpServer server, string path)
        {
            this.HttpServer = server;

            OptionsCommand optionsCommand = new OptionsCommand();
            optionsCommand.Start(this, path);
            commands.Add(optionsCommand);

            PropFindCommand propfindCommand = new PropFindCommand();
            propfindCommand.Start(this, path);
            commands.Add(propfindCommand);

            LockCommand lockCommand = new LockCommand();
            lockCommand.Start(this, path);
            commands.Add(lockCommand);

            UnlockCommand unlockCommand = new UnlockCommand();
            unlockCommand.Start(this, path);
            commands.Add(unlockCommand);

            MkcolCommand mkcolCommand = new MkcolCommand();
            mkcolCommand.Start(this, path);
            commands.Add(mkcolCommand);

            MoveCommand moveCommand = new MoveCommand();
            moveCommand.Start(this, path);
            commands.Add(moveCommand);

            GetCommand getCommand = new GetCommand();
            getCommand.Start(this, path);
            commands.Add(getCommand);

            PutCommand putCommand = new PutCommand();
            putCommand.Start(this, path);
            commands.Add(putCommand);

            DeleteCommand deleteCommand = new DeleteCommand();
            deleteCommand.Start(this, path);
            commands.Add(deleteCommand);

            CopyCommand copyCommand = new CopyCommand();
            copyCommand.Start(this, path);
            commands.Add(copyCommand);

            PropPatchCommand proppatchCommand = new PropPatchCommand();
            proppatchCommand.Start(this, path);
            commands.Add(proppatchCommand);
        }

        internal IList<IWebDAVResource> OnPropFindConnector(string username, string path, DepthHeader depth)
        {
            if (OnPropFind != null)
            {
                try { return OnPropFind(username, path, depth); }
                catch (Exception ex)
                {
                    Console.WriteLine("Caught exception in OnPropFind callback: " + ex);
                }
            }
            
            return new List<IWebDAVResource>(0);
        }

        internal bool AuthenticateRequest(OSHttpRequest request, OSHttpResponse response, out string username)
        {
            username = String.Empty;
            bool authSuccess = true;

            if (Authentication != AuthenticationType.None)
            {
                authSuccess = false;

                string[] authHeaderValues = request.Headers.GetValues("Authorization");
                string authHeader = (authHeaderValues != null && authHeaderValues.Length > 0) ?
                    authHeader = authHeaderValues[0] :
                    authHeader = String.Empty;

                switch (Authentication)
                {
                    case AuthenticationType.Basic:
                        if (authHeader.StartsWith("Basic"))
                        {
                            string basicStr = Encoding.ASCII.GetString(Convert.FromBase64String(authHeader.Substring(6)));
                            int colonPos = basicStr.LastIndexOf(':');
                            if (colonPos > -1)
                            {
                                username = basicStr.Substring(0, colonPos);
                                string password = basicStr.Substring(colonPos + 1);

                                if (OnBasicAuthenticate != null)
                                    authSuccess = OnBasicAuthenticate(username, password);
                            }
                        }
                        break;
                    case AuthenticationType.Digest:
                        authHeader = authHeader.Trim();
                        if (authHeader.StartsWith("Digest"))
                        {
                            Dictionary<string, string> digestives = new Dictionary<string, string>();
                            digestives = GetHeaderParts(authHeader);

                            if (digestives["uri"] != HttpUtility.UrlPathEncode(request.Url.PathAndQuery))
                            {
                                response.StatusCode = (int)HttpStatusCode.BadRequest;
                                return false;
                            }

                            string passwd = null;
                            PasswordFormat format;
                            if (OnDigestAuthenticate != null)
                            {
                                if (!OnDigestAuthenticate(digestives["username"], out format, out passwd))
                                {
                                    authSuccess = false; //failed to find password
                                    break;
                                }
                            }
                            else
                            {
                                //no password request handler
                                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                                return false;
                            }

                            string ha1 = null;
                            username = digestives["username"];
                            //For some unknown horrible reason, windows vista client gives username in format DOMAIN\USER, but
                            //calculates the MD5 hash with format DOMAIN\USER. To prevent it from failing well need to convert
                            //the username to proper format. For more info
                            //see: http://www.webdavsystem.com/server/documentation/authentication/digest_vista_xp_2003
                            if (request.Headers["User-Agent"] != null && request.Headers["User-Agent"].StartsWith("Microsoft-WebDAV-MiniRedir/6") && username.Contains("\\\\"))
                            {
                                string[] usernameParts = username.Split('\\');
                                username = usernameParts[0] + "\\" + usernameParts[2];
                            }

                            if (format == PasswordFormat.Plain)
                                ha1 = MD5HashString(username + ":" + digestives["realm"] + ":" + passwd);
                            else
                                ha1 = passwd;

                            string ha2 = null;
                            if (digestives["qop"] != null && digestives["qop"] == "auth-int")
                            {
                                string entityHash = MD5HashString(request.InputStream.ToString());
                                ha2 = MD5HashString(request.HttpMethod + ":" + digestives["uri"] + ":" + entityHash);
                            }
                            else
                                ha2 = MD5HashString(request.HttpMethod + ":" + digestives["uri"]);

                            string myResponse = null;
                            if (digestives["qop"] != null && (digestives["qop"] == "auth-int" || digestives["qop"] == "auth"))
                                myResponse = MD5HashString(
                                    ha1 + ":" +
                                    digestives["nonce"] + ":" +
                                    digestives["nc"] + ":" +
                                    digestives["cnonce"] + ":" +
                                    digestives["qop"] + ":" +
                                    ha2);
                            else
                                myResponse = MD5HashString(ha1 + ":" + digestives["nonce"] + ":" + ha2);

                            if (myResponse == digestives["response"] && 
                                IsValidNonce(digestives["nonce"]))
                                authSuccess = true;
                            else
                                authSuccess = false;
                            break;

                        }
                        authSuccess = false;
                        break;
                }
            }

            if (authSuccess)
            {
                return true;
            }
            else
            {
                byte[] responseData = Encoding.UTF8.GetBytes("401 Access Denied");
                response.StatusCode = (int)HttpStatusCode.Unauthorized;
                response.StatusDescription = "Access Denied";
                response.ContentLength = responseData.Length;
                response.Body.Write(responseData, 0, responseData.Length);

                if (Authentication == AuthenticationType.Digest)
                {
                    string realm = request.Url.Host;
                    string nonce = GenerateNonce();

                    //qop indicates that the server is merely looking for the client to authenticate
                    //may ask for message integrity as well (qop=auth-int)
                    string qop = "auth";

                    //advices if the client on whether it's appropriate to prompt the user for the password again or use a cached value
                    bool stale = false; //send stale=true when it needs the client to rechallenge the user.

                    //construct the digest header value
                    string digest = "Digest realm=\"" + realm + "\",";
                    digest += " stale=" + stale.ToString() + ",";
                    digest += " nonce=\"" + nonce + "\",";
                    digest += " qop=\"" + qop + "\", algorithm=\"MD5\"";
                    response.AddHeader("WWW-Authenticate", digest);
                }
                else
                {
                    response.AddHeader("WWW-Authenticate", "Basic Realm=\"\"");
                }

                return false;
            }
        }

        private Dictionary<string,string> GetHeaderParts(string authorizationHeader)
        {
            // A method, that converts 
            // HTTP header string with all its contents to Dictionary
            Dictionary<string, string> dict = new Dictionary<string, string>();
            string[] parts =
               authorizationHeader.Substring(7).Split(new char[] { ',' });
            foreach (string part in parts)
            {
                string[] subParts = part.Split(new char[] { '=' }, 2);
                string key = subParts[0].Trim(new char[] { ' ', '\"' });
                string val = subParts[1].Trim(new char[] { ' ', '\"' });
                dict.Add(key, val);
            }
            return dict;
        }

        private string MD5HashString(string Value)
        {
            System.Security.Cryptography.MD5CryptoServiceProvider x = new System.Security.Cryptography.MD5CryptoServiceProvider();
            byte[] data = System.Text.Encoding.ASCII.GetBytes(Value);
            data = x.ComputeHash(data);
            string ret = "";
            for (int i = 0; i < data.Length; i++)
                ret += String.Format("{0:x02}", data[i]); //data[i].ToString("x2").ToLower();
            return ret;
        }

        protected virtual string GenerateNonce()
        {
            // Create a unique nonce - 
            // the simpliest version
            // Now + 3 minutes, encoded base64
            // The nonce validity check 
            // will be performed also against the time
            // More strong example of nonce - 
            // use additionally ETag and unique key, which is
            // known by the server
            DateTime nonceTime =
               DateTime.Now + TimeSpan.FromMinutes(3);
            string expireStr = nonceTime.ToString("G");

            Encoding enc = new ASCIIEncoding();
            byte[] expireBytes = enc.GetBytes(expireStr);
            string nonce = Convert.ToBase64String(expireBytes);

            // base64 adds "=" 
            // sign, which is forbidden by the server
            // cut it
            nonce = nonce.TrimEnd('=');
            return nonce;
        }

        protected virtual bool IsValidNonce(string nonce)
        {
            // Check nonce validity
            // decode from base64 and check
            // This implementation uses a simple version - 
            // thats why the check is simple also -
            // check against the time

            DateTime expireTime;
            int numPadChars = nonce.Length % 4;
            if (numPadChars > 0)
                numPadChars = 4 - numPadChars;
            string newNonce =
               nonce.PadRight(nonce.Length + numPadChars, '=');

            try
            {
                byte[] decodedBytes =
                   Convert.FromBase64String(newNonce);
                string preNonce =
                   new ASCIIEncoding().GetString(decodedBytes);
                expireTime = DateTime.Parse(preNonce);
            }
            catch (FormatException)
            {
                return false;
            }
            return (expireTime >= DateTime.Now);
        }

        public string GetAvailableMethods()
        {
            string allowHeader = String.Empty;
            if (OnCopy != null)
            {
                allowHeader += "COPY, ";
            }
            if (OnDelete != null)
            {
                allowHeader += "DELETE, ";
            }
            if (OnGet != null)
            {
                allowHeader += "GET, ";
            }
            //this doesn't exist yet
            //TODO: implement HEAD method
            //if (OnHead != null)
            //{
            //    allowHeader += "HEAD, ";
            //}
            if (OnLock != null)
            {
                allowHeader += "LOCK, ";
            }
            if (OnNewCol != null)
            {
                allowHeader += "MKCOL, ";
            }
            if (OnMove != null)
            {
                allowHeader += "MOVE, ";
            }
            allowHeader += "OPTIONS, ";
            if (OnPropFind != null)
            {
                allowHeader += "PROPFIND, ";
            }
            if (OnPropPatch != null)
            {
                allowHeader += "PROPPATCH, ";
            }
            if (OnPut != null)
            {
                allowHeader += "PUT, ";
            }
            if (OnUnlock != null)
            {
                allowHeader += "UNLOCK, ";
            }

            int k = allowHeader.LastIndexOf(',');
            allowHeader = allowHeader.Remove(k);
            return allowHeader;
        }

        internal WebDAVLockResponse OnLockConnector(WebDAVLockRequest request)
        {
            if (OnLock != null)
            {
                try { return OnLock(request); }
                catch (Exception ex)
                {
                    Console.WriteLine("Caught exception in OnLock callback: " + ex);
                }
            }
            return null;
        }

        internal HttpStatusCode OnUnlockConnector(string path, string locktoken, string username)
        {
            if (OnUnlock != null)
            {
                try { return OnUnlock(path, locktoken, username); }
                catch (Exception ex)
                {
                    Console.WriteLine("Caught exception in OnUnlock callback: " + ex);
                }
            }
            return HttpStatusCode.InternalServerError;
        }

        internal HttpStatusCode CreateCollection(string path, string username, string[] ifheaders)
        {
            if (OnNewCol != null)
            {
                return OnNewCol(path, username, ifheaders);
            }
            return HttpStatusCode.MethodNotAllowed;
        }

        internal HttpStatusCode OnMoveConnector(string username, Uri uri, string destination, DepthHeader depth, bool overwrite, string[] if_headers, out Dictionary<String, HttpStatusCode> multiStatusValues)
        {
            if (OnMove != null)
            {
                return OnMove(username, uri, destination, depth, overwrite, if_headers, out multiStatusValues);
            }
            multiStatusValues = null;
            return HttpStatusCode.MethodNotAllowed;
        }

        internal HttpStatusCode GET(OSHttpResponse response, string path, string username)
        {
            if (OnGet != null)
            {
                return OnGet(response, path, username);
            }
            return HttpStatusCode.MethodNotAllowed;
        }

        internal HttpStatusCode PutResource(OSHttpRequest request, string path, string username, string[] ifHeaders)
        {
            if (OnPut != null)
            {
                return OnPut(request, path, username, ifHeaders);
            }
            return HttpStatusCode.Forbidden;
        }

        internal HttpStatusCode DeleteResource(Uri uri, string username, out Dictionary<string, HttpStatusCode> multiStatus, string[] ifHeaders)
        {
            if (OnDelete != null)
            {
                return OnDelete(uri, username, out multiStatus, ifHeaders);
            }
            multiStatus = null;
            return HttpStatusCode.Forbidden;
        }

        internal HttpStatusCode OnCopyConnector(string username, Uri uri, string destination, DepthHeader depth, bool overwrite, string[] ifHeaders, out Dictionary<string, HttpStatusCode> multiStatusValues)
        {
            if (OnCopy != null)
            {
                return OnCopy(username, uri, destination, depth, overwrite, ifHeaders, out multiStatusValues);
            }
            multiStatusValues = null;
            return HttpStatusCode.Forbidden;
        }

        internal HttpStatusCode OnPropPatchConnector(string username, Uri uri, string uriPath, string nspace, Dictionary<string, string> setProperties, List<string> removeProperties, out Dictionary<string, HttpStatusCode> multiStatus, string[] ifHeaders)
        {
            if (OnPropPatch != null)
            {
                return OnPropPatch(username, uri, uriPath, nspace, setProperties, removeProperties, out multiStatus, ifHeaders);
            }
            multiStatus = null;
            return HttpStatusCode.InternalServerError;
        }
    }
}
