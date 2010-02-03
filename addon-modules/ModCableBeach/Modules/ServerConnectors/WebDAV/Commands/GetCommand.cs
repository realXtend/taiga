using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using OpenSim.Framework.Servers.HttpServer;

namespace ModCableBeach
{
    public class GetCommand : ICommand
    {
        WebDAVListener server;

        #region ICommand Members

        public void Start(WebDAVListener server, string path)
        {
            this.server = server;
            server.HttpServer.AddStreamHandler(new StreamHandler("GET", path, GetHandler));
        }

        #endregion

        byte[] GetHandler(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            string username;
            if (server.AuthenticateRequest(httpRequest, httpResponse, out username))
            {
                System.Net.HttpStatusCode status = server.GET(httpResponse, httpRequest.Url.AbsolutePath, username);
                httpResponse.StatusCode = (int)status;
            }

            return OpenMetaverse.Utils.EmptyBytes;
        }
    }
}
