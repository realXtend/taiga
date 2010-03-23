using System;
using System.Collections.Generic;
using System.Net;
using OpenMetaverse;
using OpenMetaverse.StructuredData;


/// <summary>
/// Fast and dirty copy paste from CableBeach project
/// </summary>

namespace OpenSim.Grid.UserServer.Modules.RexLogin
{
    public static class RexAvatarAttributes
    {
        public static readonly Uri AUTH_URI = new Uri("http://www.realxtend.org/attributes/authURI");
        public static readonly Uri ACCOUNT = new Uri("http://www.realxtend.org/attributes/account");
        public static readonly Uri REALNAME = new Uri("http://www.realxtend.org/attributes/realname");
        public static readonly Uri SESSIONHASH = new Uri("http://www.realxtend.org/attributes/sessionhash");
        public static readonly Uri AVATAR_STORAGE_URL = new Uri("http://www.realxtend.org/attributes/avatarStorageUri");
        public static readonly Uri SKYPE_URL = new Uri("http://www.realxtend.org/attributes/skypeUrl");
        public static readonly Uri GRID_URL = new Uri("http://www.realxtend.org/attributes/gridUrl");
    }


    public class Service
    {
        public Uri Identifier;
        public Uri XrdDocument;
        public Uri SeedCapability;
        public Uri OAuthRequestToken;
        public Uri OAuthAuthorizeToken;
        public Uri OAuthGetAccessToken;
        public bool IsTrusted;
        public bool CanOverride;
        public Dictionary<Uri, Uri> Capabilities;

        public Service(Uri identifier, Uri xrdDocument, Uri seedCapability, Uri oAuthRequestToken, Uri oAuthAuthorizeToken, Uri oAuthGetAccessToken,
            bool isTrusted, bool canOverride, Dictionary<Uri, Uri> capabilities)
        {
            Identifier = identifier;
            XrdDocument = xrdDocument;
            SeedCapability = seedCapability;
            OAuthRequestToken = oAuthRequestToken;
            OAuthAuthorizeToken = oAuthAuthorizeToken;
            OAuthGetAccessToken = oAuthGetAccessToken;
            IsTrusted = isTrusted;
            CanOverride = canOverride;
            this.Capabilities = new Dictionary<Uri, Uri>(capabilities);
        }

        public Service(Uri identifier, Uri xrdDocument, Uri seedCapability, Uri oAuthRequestToken, Uri oAuthAuthorizeToken, Uri oAuthGetAccessToken,
            bool isTrusted, bool canOverride)
            : this(identifier, xrdDocument, seedCapability, oAuthRequestToken, oAuthAuthorizeToken, oAuthGetAccessToken, isTrusted, canOverride, new Dictionary<Uri, Uri>())
        {
        }

        public Service(Service service)
        {
            Identifier = service.Identifier;
            XrdDocument = service.XrdDocument;
            SeedCapability = service.SeedCapability;
            OAuthRequestToken = service.OAuthRequestToken;
            OAuthAuthorizeToken = service.OAuthAuthorizeToken;
            OAuthGetAccessToken = service.OAuthGetAccessToken;
            IsTrusted = service.IsTrusted;
            CanOverride = service.CanOverride;
            this.Capabilities = new Dictionary<Uri, Uri>(service.Capabilities);
        }

        public Uri[] GetUnassociatedCapabilities()
        {
            List<Uri> unassociated = new List<Uri>();

            foreach (KeyValuePair<Uri, Uri> cap in Capabilities)
            {
                if (cap.Value == null)
                    unassociated.Add(cap.Key);
            }

            return unassociated.ToArray();
        }

        public bool TryGetCapability(Uri capIdentifier, out Uri location)
        {
            if (Capabilities.TryGetValue(capIdentifier, out location) && location != null)
                return true;
            return false;
        }

        public override string ToString()
        {
            bool first = true;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<Uri, Uri> entry in Capabilities)
            {
                if (first)
                    first = false;
                else
                    sb.Append(',');

                sb.Append(entry.Key.Segments[entry.Key.Segments.Length - 1]);

                if (entry.Value != null)
                    sb.Append(":" + entry.Value);
            }

            bool trusted = IsTrusted;
            string location;
            if (trusted)
                location = SeedCapability.ToString();
            else
                location = (OAuthGetAccessToken != null) ? OAuthGetAccessToken.ToString() : "null";

            return String.Format("{0} (Location: {1}, {2} {3}, Capabilities: {4})",
                Identifier,
                location,
                trusted ? "Trusted" : "Untrusted",
                CanOverride ? "CanOverride" : "NoOverride",
                sb.ToString());
        }
    }

    public class ServiceCollection : Dictionary<Uri, Service>
    {
        public ServiceCollection()
        {
        }

        public ServiceCollection(IEnumerable<Service> services)
        {
            foreach (Service service in services)
                this[service.Identifier] = service;
        }

        public Dictionary<Uri, Dictionary<Uri, Uri>> ToMessageDictionary()
        {
            Dictionary<Uri, Dictionary<Uri, Uri>> d = new Dictionary<Uri, Dictionary<Uri, Uri>>(this.Count);

            foreach (KeyValuePair<Uri, Service> serviceEntry in this)
                d.Add(serviceEntry.Key, serviceEntry.Value.Capabilities);

            return d;
        }
    }

}