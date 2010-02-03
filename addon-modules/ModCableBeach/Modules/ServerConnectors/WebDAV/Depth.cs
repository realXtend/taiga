using System;
using System.Collections.Generic;
using System.Text;
using OpenSim.Framework.Servers.HttpServer;

namespace ModCableBeach
{
    /// <summary>
    /// See: http://msdn.microsoft.com/en-us/library/aa142852(EXCHG.65).aspx
    /// </summary>
    public enum DepthHeader
    {
        InfinityNoRoot = -3, //Microsoft stuff, not standard
        Infinity = -2,
        OneNoRoot = -1, //Microsoft stuff, not standard
        Zero = 0,
        One = 1
    };

    public static class Depth
    {
        public static DepthHeader ParseDepth(OSHttpRequest request)
        {
            //Clients may submit a Depth Header with a value of "0", "1", "1,noroot" or "infinity".
            //A PROPFIND Method without a Depth Header acts as if a Depth Header value of "infinity" was included.
            string[] depths = request.Headers.GetValues("depth");
            if (depths == null)
            {
                //no depth header found
                throw new ArgumentException("No depth header found");
            }
            if (depths.Length == 1)
            {
                if (depths[0].ToLower() == "infinity")
                {
                    return DepthHeader.Infinity;
                }
                else if (depths[0] == "1")
                {
                    return DepthHeader.One;
                }
                else if (depths[0] == "0")
                {
                    return DepthHeader.Zero;
                }
                else
                {
                    //Unknown value
                    Console.WriteLine("Unknown depth value " + depths[0]);
                    throw new ArgumentException("Unknown value in depth header");
                }
            }
            else if (depths.Length == 2)
            {
                //noroot stuff
                if (depths[0].ToLower() == "infinity" && depths[1].ToLower() == "noroot")
                {
                    return DepthHeader.InfinityNoRoot;
                }
                else if (depths[0] == "1" && depths[1].ToLower() == "noroot")
                {
                    return DepthHeader.OneNoRoot;
                }
                else
                {
                    //Unknown value
                    Console.WriteLine("Unknown depth values {0}, {1}", depths[0], depths[1]);
                    throw new ArgumentException("Unknown values in depth header");
                }
            }
            else
            {
                //something awful has happened. depth should not contain more than two values
                throw new ArgumentException("More than two arquments in Depth header");
            }
        }
    }
}
