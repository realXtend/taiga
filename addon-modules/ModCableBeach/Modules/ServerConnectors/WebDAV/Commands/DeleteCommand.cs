using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Xml;
using System.IO;
using OpenSim.Framework.Servers.HttpServer;

namespace ModCableBeach
{
    public class DeleteCommand : ICommand
    {
        WebDAVListener server;

        #region ICommand Members

        public void Start(WebDAVListener server, string path)
        {
            this.server = server;
            server.HttpServer.AddStreamHandler(new StreamHandler("DELETE", path, DeleteHandler));
        }

        #endregion

        byte[] DeleteHandler(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            string username;
            if (server.AuthenticateRequest(httpRequest, httpResponse, out username))
            {
                Dictionary<string, HttpStatusCode> multiStatus = null;
                HttpStatusCode status = server.DeleteResource(httpRequest.Url, username, out multiStatus);
                httpResponse.StatusCode = (int)status;
                if (status == (HttpStatusCode)207 && multiStatus != null) //multiple status
                {
                    byte[] bytes = XmlResponse.WriteMultiStatusResponseBody(multiStatus);
                    httpResponse.ContentLength = bytes.Length;
                    return bytes;
                }
                else
                {
                    httpResponse.StatusCode = (int)HttpStatusCode.InternalServerError;
                    return OpenMetaverse.Utils.EmptyBytes;
                }
            }

            return OpenMetaverse.Utils.EmptyBytes;
        }
    }
}
