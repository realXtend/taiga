/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using DotNetOpenAuth.OAuth;
using DotNetOpenAuth.OAuth.Messages;
using DotNetOpenAuth.OAuth.ChannelElements;

namespace ModCableBeach
{
    public class InMemoryProviderTokenManager : IServiceProviderTokenManager
    {
        #region Classes

        private class TokenInfo : IServiceProviderRequestToken
        {
            internal TokenInfo()
            {
                this.CreatedOn = DateTime.Now;
            }

            public string ConsumerKey { get; set; }
            public DateTime CreatedOn { get; set; }
            public string Token { get; set; }
            public string VerificationCode { get; set; }
            public Uri Callback { get; set; }
            public Version ConsumerVersion { get; set; }
            internal string Secret { get; set; }
        }

        private class ConsumerInfo : IConsumerDescription
        {
            public string Key { get; set; }
            public string Secret { get; set; }
            public X509Certificate2 Certificate { get; set; }
            public Uri Callback { get; set; }
            public VerificationCodeFormat VerificationCodeFormat { get; set; }
            public int VerificationCodeLength { get; set; }
        }

        #endregion Classes

        private Dictionary<string, TokenInfo> tokens = new Dictionary<string, TokenInfo>();

        /// <summary>
        /// Request tokens that have been issued, and whether they have been authorized yet.
        /// </summary>
        private Dictionary<string, bool> requestTokens = new Dictionary<string, bool>();

        /// <summary>
        /// Access tokens that have been issued and have not yet expired.
        /// </summary>
        private Dictionary<string, string> accessTokens = new Dictionary<string, string>();

        #region ITokenManager Members

        public string GetTokenSecret(string token)
        {
            return this.tokens[token].Secret;
        }

        public void StoreNewRequestToken(UnauthorizedTokenRequest request, ITokenSecretContainingMessage response)
        {
            this.tokens.Add(response.Token, new TokenInfo { ConsumerKey = request.ConsumerKey, Token = response.Token, Secret = response.TokenSecret });
            this.requestTokens.Add(response.Token, false);
        }

        /// <summary>
        /// Checks whether a given request token has already been authorized
        /// by some user for use by the Consumer that requested it.
        /// </summary>
        /// <param name="requestToken">The Consumer's request token.</param>
        /// <returns>
        /// True if the request token has already been fully authorized by the user
        /// who owns the relevant protected resources.  False if the token has not yet
        /// been authorized, has expired or does not exist.
        /// </returns>
        public bool IsRequestTokenAuthorized(string requestToken)
        {
            return this.requestTokens[requestToken];
        }

        public void ExpireRequestTokenAndStoreNewAccessToken(string consumerKey, string requestToken, string accessToken, string accessTokenSecret)
        {
            this.requestTokens.Remove(requestToken);
            this.accessTokens.Add(accessToken, accessToken);
            this.tokens.Remove(requestToken);
            this.tokens.Add(accessToken, new TokenInfo { Token = accessToken, Secret = accessTokenSecret });
        }

        /// <summary>
        /// Classifies a token as a request token or an access token.
        /// </summary>
        /// <param name="token">The token to classify.</param>
        /// <returns>Request or Access token, or invalid if the token is not recognized.</returns>
        public TokenType GetTokenType(string token)
        {
            if (this.requestTokens.ContainsKey(token))
                return TokenType.RequestToken;
            else if (this.accessTokens.ContainsKey(token))
                return TokenType.AccessToken;
            else
                return TokenType.InvalidToken;
        }

        #endregion

        #region IServiceProviderTokenManager Members

        public IConsumerDescription GetConsumer(string consumerKey)
        {
            // We don't keep a list of authorized consumers, everyone is welcome
            ConsumerInfo description = new ConsumerInfo();
            description.Key = consumerKey;
            return description;
        }

        public IServiceProviderRequestToken GetRequestToken(string token)
        {
            try { return this.tokens[token]; }
            catch (Exception) { throw new KeyNotFoundException("Unrecognized token"); }
        }

        public IServiceProviderAccessToken GetAccessToken(string token)
        {
            try { return (IServiceProviderAccessToken)this.tokens[token]; }
            catch (Exception) { throw new KeyNotFoundException("Unrecognized token"); }
        }

        #endregion

        /// <summary>
        /// Marks an existing token as authorized.
        /// </summary>
        /// <param name="requestToken">The request token.</param>
        public void AuthorizeRequestToken(string requestToken)
        {
            if (requestToken == null)
                throw new ArgumentNullException("requestToken");

            this.requestTokens[requestToken] = true;
        }

        public void UpdateToken(IServiceProviderRequestToken token)
        {
            try
            {
                TokenInfo tokenInfo = this.tokens[token.Token];
                tokenInfo.Callback = token.Callback;
                tokenInfo.ConsumerKey = token.ConsumerKey;
                tokenInfo.ConsumerVersion = token.ConsumerVersion;
                tokenInfo.CreatedOn = token.CreatedOn;
                tokenInfo.VerificationCode = token.VerificationCode;
            }
            catch (Exception) { throw new KeyNotFoundException("Unrecognized token"); }
        }
    }
}
