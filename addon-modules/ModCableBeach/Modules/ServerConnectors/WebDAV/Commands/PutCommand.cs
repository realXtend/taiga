using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using OpenSim.Framework.Servers.HttpServer;

namespace ModCableBeach
{
    public class PutCommand : ICommand
    {
        WebDAVListener server;

        #region ICommand Members

        public void Start(WebDAVListener server, string path)
        {
            this.server = server;
            server.HttpServer.AddStreamHandler(new StreamHandler("PUT", path, PutHandler));
        }

        #endregion

        byte[] PutHandler(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            string username;
            if (server.AuthenticateRequest(httpRequest, httpResponse, out username))
            {
                string[] ifHeaders = httpRequest.Headers.GetValues("If");
                System.Net.HttpStatusCode status = server.PutResource(httpRequest, httpRequest.Url.AbsolutePath, username, ifHeaders);
                httpResponse.StatusCode = (int)status;
            }

            return OpenMetaverse.Utils.EmptyBytes;
        }
    }
}
