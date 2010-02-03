using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Net;
using OpenSim.Framework.Servers.HttpServer;
using OpenMetaverse;

namespace ModCableBeach
{
    public class CopyCommand : ICommand
    {
        WebDAVListener server;

        #region ICommand Members

        public void Start(WebDAVListener server, string path)
        {
            this.server = server;
            server.HttpServer.AddStreamHandler(new StreamHandler("COPY", path, CopyHandler));
        }

        byte[] CopyHandler(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            string username;
            if (server.AuthenticateRequest(httpRequest, httpResponse, out username))
            {
                //parse Destination from header
                //this is required for the copy command
                string[] destinations = httpRequest.Headers.GetValues("destination");
                if (destinations == null)
                {
                    httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                    return OpenMetaverse.Utils.EmptyBytes;
                }
                string destination = destinations[0];

                //If the client includes a Depth header with an illegal value, the server should return 400 Bad Request.
                //this means that if the resource is a collection, then depth should be infinity
                //and if resource is not a collection, then the depth should be 0
                //if the depth doesn't exist, then proceed normally
                DepthHeader depth = DepthHeader.Infinity; //this is the standard default
                if (httpRequest.Headers["depth"] != null)
                {
                    depth = Depth.ParseDepth(httpRequest);
                }

                //parse Overwrite header
                //possible values: 'T' or 'F' (true or false)
                //otherwise return 400 Bad Request
                //if value is F and destination already exists, fail with response 412 Precondition Failed
                //default for this value is T
                bool overwrite = true;
                string[] overwrites = httpRequest.Headers.GetValues("overwrite");
                if (overwrites != null)
                {
                    if (overwrites[0].ToLower() == "t")
                        overwrite = true;
                    else if (overwrites[0].ToLower() == "f")
                        overwrite = false;
                    else
                    {
                        httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                        return OpenMetaverse.Utils.EmptyBytes;
                    }
                }

                //If header might contain lock tokens, we need to pass them forward too
                string[] ifHeaders = httpRequest.Headers.GetValues("if");

                Dictionary<String, HttpStatusCode> multiStatusValues = null;
                HttpStatusCode status = server.OnCopyConnector(username, httpRequest.Url, destination, depth, overwrite, ifHeaders, out multiStatusValues);
                httpResponse.StatusCode = (int)status;

                if (status == (HttpStatusCode)207 && multiStatusValues != null) //multiple status
                {
                    byte[] bytes = XmlResponse.WriteMultiStatusResponseBody(multiStatusValues);
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

        #endregion
    }
}
