using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.IO;
using OpenSim.Framework.Servers.HttpServer;

namespace ModCableBeach
{
    public class MkcolCommand : ICommand
    {
        WebDAVListener server;

        #region ICommand Members

        public void Start(WebDAVListener server, string path)
        {
            this.server = server;
            server.HttpServer.AddStreamHandler(new StreamHandler("MKCOL", path, MkcolHandler));
        }

        byte[] MkcolHandler(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            string username;
            if (server.AuthenticateRequest(httpRequest, httpResponse, out username))
            {
                System.Net.HttpStatusCode status = server.CreateCollection(httpRequest.Url.AbsolutePath, username);
                httpResponse.StatusCode = (int)status;
            }

            return OpenMetaverse.Utils.EmptyBytes;
        }

        #endregion
    }
}
