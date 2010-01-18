/*
 *
 * The contents of this file are subject to the Mozilla Public License Version 1.0 (the "License");
 * you may not use this file except in compliance with the License.
 *
 * You may obtain a copy of the License at http://www.mozilla.org/MPL/
 *
 * Software distributed under the License is distributed on an "AS IS" basis,
 * WITHOUT WARRANTY OF ANY KIND, either express or implied.
 *
 * See the License for the specific language governing rights and limitations under the License.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using System.Web;
using System.Xml;
using log4net;

namespace OpenSim.Grid.UserServer.Modules
{
    public enum FacebookValidationState
    {
        NoSignatureFound,
        InvalidSignature,
        Valid
    }

    [Serializable]
    public class FacebookConnectAuthenticationException : Exception
    {
        private FacebookValidationState m_reason;

        /// <summary>
        /// The cause of the Authentication error.
        /// </summary>
        public FacebookValidationState Reason { get { return m_reason; } }

        public FacebookConnectAuthenticationException() : base() { }

        public FacebookConnectAuthenticationException(string message) : base(message) { }

        public FacebookConnectAuthenticationException(string message, Exception innerException) : base(message, innerException) { }

        public FacebookConnectAuthenticationException(string message, FacebookValidationState reason)
            : this(message)
        {
            m_reason = reason;
        }

        protected FacebookConnectAuthenticationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            m_reason =
                (FacebookValidationState)info.GetValue("FBConnectAuthenticationException_Reason", typeof(FacebookValidationState));
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue("FBConnectAuthenticationException_Reason", this.Reason, typeof(FacebookValidationState));
        }

        public static implicit operator string(FacebookConnectAuthenticationException ex)
        {
            return ex.ToString();
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("FBConnectAuthenticationException: {0}", this.Message);
            sb.AppendFormat(" The facebook session could not be Authenticated. Reason: '{0}'", Reason);

            if (this.InnerException != null)
            {
                sb.AppendFormat(" ---> {0} <---", base.InnerException.ToString());
            }

            if (this.StackTrace != null)
            {
                sb.Append(Environment.NewLine);
                sb.Append(base.StackTrace);
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// An object representing a Facebook Connect Session
    /// </summary>
    public sealed class FacebookConnectSession
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FBConnectSession"/> class.
        /// </summary>
        internal FacebookConnectSession(int expires, string sessionKey, string sessionSecret, long userId)
        {
            Expires = OpenMetaverse.Utils.UnixTimeToDateTime(expires);
            SessionKey = sessionKey;
            SessionSecret = sessionSecret;
            UserID = userId;
        }

        /// <summary>
        /// The expiry time for the session
        /// </summary>
        public DateTime Expires;
        /// <summary>
        /// The session key used by Facebook
        /// </summary>
        public string SessionKey;
        /// <summary>
        /// The session-specific secret for this session/application tuple. Not to be confused with the Application Secret.
        /// </summary>
        public string SessionSecret;
        /// <summary>
        /// The Facebook User ID of the user.
        /// </summary>
        public long UserID;
    }

    public class FacebookConnect
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly string[] COOKIE_NAMES = new[] { "user", "session_key", "expires", "ss" };

        private readonly string appSecret;
        private readonly string apiKey;

        public string ApiKey { get { return apiKey; } }
        public string AppSecret { get { return appSecret; } }

        /// <summary>
        /// Initiliazes an instance of <see cref="FBConnectAuthentication"/>.
        /// </summary>
        /// <param name="apiKey">The apiKey of your Facebook application</param>
        /// <param name="appSecret">Your Facebook application's "secret"</param>
        public FacebookConnect(string apiKey, string appSecret)
        {
            if (String.IsNullOrEmpty(apiKey))
                throw new ArgumentException("apiKey cannot be null or empty", "apiKey");
            if (String.IsNullOrEmpty(appSecret))
                throw new ArgumentException("appSecret cannot be null or empty", "appSecret");

            this.appSecret = appSecret;
            this.apiKey = apiKey.ToLowerInvariant();
        }

        /// <summary>
        /// Validates that the request came from a validated Facebook user.
        /// </summary>
        /// <param name="cookies">A collection of cookies containing the Facebook Connect auth cookies.</param>
        /// <returns>A <see cref="ValidationState"/> value, indicating whether the request was valid.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the <paramref name="cookies"/> parameter is null.</exception>
        public FacebookValidationState Validate(HttpCookieCollection cookies)
        {
            if (cookies == null)
                throw new ArgumentNullException("cookies");

            // See http://wiki.developers.facebook.com/index.php/Verifying_The_Signature for more info
            string signature = GetSignatureFromCookies(cookies);

            if (signature == null)
                return FacebookValidationState.NoSignatureFound;

            SortedList<string, string> sortedCookieValues = ExtractAndSortFBCookies(cookies);

            var sb = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in sortedCookieValues)
                sb.AppendFormat("{0}={1}", pair.Key, pair.Value);

            sb.Append(appSecret);
            string stringToHash = sb.ToString();

            StringBuilder computedHash = new StringBuilder();
            byte[] hash = MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(stringToHash));
            foreach (byte b in hash)
                computedHash.AppendFormat("{0:x2}", b);

            bool isSigValid = computedHash.ToString().ToLowerInvariant() == signature.ToLowerInvariant();
            return isSigValid ? FacebookValidationState.Valid : FacebookValidationState.InvalidSignature;
        }

        private SortedList<string, string> ExtractAndSortFBCookies(HttpCookieCollection cookies)
        {
            var result = new SortedList<string, string>();
            string cookiePrefix = apiKey + "_";
            foreach (string cookieName in cookies)
            {
                if (cookieName.StartsWith(cookiePrefix))
                {
                    var cookie = cookies[cookieName];
                    result.Add(cookie.Name.Substring(cookiePrefix.Length), cookie.Value);
                }
            }

            return result;
        }

        private string GetSignatureFromCookies(HttpCookieCollection cookies)
        {
            var sigCookie = cookies[apiKey];
            return sigCookie != null ? sigCookie.Value : null;
        }

        /// <summary>
        /// Gets a FB Connect session from the values in the <paramref name="cookies"/>.
        /// </summary>
        /// <param name="cookies">Cookies containing FB Connect session information</param>
        /// <returns>A FB Connect Session object</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="cookies"/> is null.</exception>
        /// <exception cref="FBConnectAuthenticationException">Thrown when the signature cannot be validated.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the expires cookie value is not a valid integer.</exception>
        public FacebookConnectSession GetSession(HttpCookieCollection cookies)
        {
            if (cookies == null)
                throw new ArgumentNullException("cookies");

            FacebookValidationState validationState = Validate(cookies);
            if (validationState != FacebookValidationState.Valid)
                throw new FacebookConnectAuthenticationException("Cannot create FB Connect session.", validationState);

            return CreateFBSessionFromCookies(cookies);
        }

        private FacebookConnectSession CreateFBSessionFromCookies(HttpCookieCollection cookies)
        {
            string cookiePrefix = apiKey + "_";
            int expires;
            if (!Int32.TryParse(cookies[cookiePrefix + "expires"].Value, out expires))
                throw new ArgumentOutOfRangeException("cookies", "The value of 'expires' cookie is not a valid integer.");

            long userId;
            if (!Int64.TryParse(cookies[cookiePrefix + "user"].Value, out userId))
                throw new ArgumentOutOfRangeException("cookies", "The value of 'user' cookie is not a valid 64-bit integer.");

            string sessionKey = cookies[cookiePrefix + "session_key"].Value;
            string sessionSecret = cookies[cookiePrefix + "ss"].Value;
            return new FacebookConnectSession(expires, sessionKey, sessionSecret, userId);
        }

        public static Dictionary<string, string> GetUserInfo(string apiKey, string appSecret, string sessionKey, long userID)
        {
            Dictionary<string, string> userInfo = new Dictionary<string, string>();

            HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create("http://api.facebook.com/restserver.php");
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";

            Dictionary<string, string> parameters = new Dictionary<string, string>();
            parameters.Add("method", "facebook.users.getInfo");
            parameters.Add("uids", userID.ToString());
            parameters.Add("fields", "first_name, last_name, profile_url, proxied_email");
            parameters.Add("session_key", sessionKey);
            parameters.Add("api_key", apiKey);
            parameters.Add("v", "1.0");
            parameters.Add("call_id", DateTime.Now.Ticks.ToString("x", CultureInfo.InvariantCulture));
            parameters.Add("sig", GenerateSignature(parameters, appSecret));

            // Convert the dictionary to an HTTP POST query
            StringBuilder query = new StringBuilder();
            foreach (KeyValuePair<string, string> entry in parameters)
                query.Append(entry.Key + "=" + HttpUtility.UrlEncode(entry.Value) + "&");
            query.Remove(query.Length - 1, 1);

            byte[] requestData = Encoding.ASCII.GetBytes(query.ToString());
            request.ContentLength = requestData.Length;

            try
            {
                using (Stream writeStream = request.GetRequestStream())
                {
                    writeStream.Write(requestData, 0, requestData.Length);
                }

                HttpWebResponse response = (HttpWebResponse)request.GetResponse();

                using (Stream readStream = response.GetResponseStream())
                {
                    XmlTextReader reader = new XmlTextReader(readStream);
                    reader.WhitespaceHandling = WhitespaceHandling.None;
                    reader.MoveToContent();

                    if (reader.ReadToFollowing("first_name"))
                    {
                        while (reader.IsStartElement())
                            userInfo.Add(reader.Name, reader.ReadElementContentAsString());
                    }
                }
            }
            catch (Exception ex)
            {
                m_log.Error("[FACEBOOK CONNECT]: Error retrieving user info: " + ex.Message);
                return null;
            }

            return userInfo;
        }

        static string GenerateSignature(IDictionary<string, string> parameters, string appSecret)
        {
            var signatureBuilder = new StringBuilder();

            // Sort the keys of the method call in alphabetical order
            var keyList = ParameterDictionaryToList(parameters);
            keyList.Sort();

            // Append all the parameters to the signature input paramaters
            foreach (string key in keyList)
                signatureBuilder.Append(String.Format(CultureInfo.InvariantCulture, "{0}={1}", key, parameters[key]));

            // Append the secret to the signature builder
            signatureBuilder.Append(appSecret);

            var md5 = MD5.Create();
            // Compute the MD5 hash of the signature builder
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(signatureBuilder.ToString().Trim()));

            // Reinitialize the signature builder to store the actual signature
            signatureBuilder = new StringBuilder();

            // Append the hash to the signature
            foreach (var hashByte in hash)
                signatureBuilder.Append(hashByte.ToString("x2", CultureInfo.InvariantCulture));

            return signatureBuilder.ToString();
        }

        static List<string> ParameterDictionaryToList(IEnumerable<KeyValuePair<string, string>> parameterDictionary)
        {
            var parameters = new List<string>();

            foreach (var kvp in parameterDictionary)
                parameters.Add(String.Format(CultureInfo.InvariantCulture, "{0}", kvp.Key));

            return parameters;
        }
    }
}
