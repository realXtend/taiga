using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Net;
using OpenSim.Framework.Servers.HttpServer;

namespace ModCableBeach
{
    public class UnlockCommand : ICommand
    {
        WebDAVListener server;

        #region ICommand Members

        public void Start(WebDAVListener server, string path)
        {
            this.server = server;
            server.HttpServer.AddStreamHandler(new StreamHandler("UNLOCK", path, UnlockHandler));
        }

        #endregion

        byte[] UnlockHandler(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            string username;
            if (server.AuthenticateRequest(httpRequest, httpResponse, out username))
            {
                string locktoken = (httpRequest.Headers.GetValues("Lock-Token"))[0];
                if (locktoken == null || locktoken == String.Empty)
                {
                    httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                }
                else
                {
                    HttpStatusCode code = server.OnUnlockConnector(httpRequest.Url.AbsolutePath, locktoken, username);
                    httpResponse.StatusCode = (int)code;
                }
            }
            else
            {
                httpResponse.StatusCode = (int)HttpStatusCode.Unauthorized;
            }

            return OpenMetaverse.Utils.EmptyBytes;
        }
    }
}
