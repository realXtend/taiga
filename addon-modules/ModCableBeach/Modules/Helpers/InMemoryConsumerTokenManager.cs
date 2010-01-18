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
using DotNetOpenAuth.OAuth.ChannelElements;
using DotNetOpenAuth.OAuth.Messages;

namespace ModCableBeach
{
    public class InMemoryConsumerTokenManager : IConsumerTokenManager
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

        private string consumerKey;
        private string consumerSecret;
        private Dictionary<string, TokenInfo> tokens = new Dictionary<string, TokenInfo>();

        /// <summary>
        /// Request tokens that have been issued, and whether they have been authorized yet.
        /// </summary>
        private Dictionary<string, bool> requestTokens = new Dictionary<string, bool>();

        /// <summary>
        /// Access tokens that have been issued and have not yet expired.
        /// </summary>
        private Dictionary<string, string> accessTokens = new Dictionary<string, string>();

        public InMemoryConsumerTokenManager(string consumerKey, string consumerSecret)
        {
            this.consumerKey = consumerKey;
            this.consumerSecret = consumerSecret;
        }

        #region IConsumerTokenManager Members

        public string ConsumerKey
        {
            get { return consumerKey; }
        }

        public string ConsumerSecret
        {
            get { return consumerSecret; }
        }

        #endregion

        #region ITokenManager Members

        public string GetTokenSecret(string token)
        {
            string tokenFixed = token.Replace(' ', '+');
            return this.tokens[tokenFixed].Secret;
        }

        public void StoreNewRequestToken(UnauthorizedTokenRequest request, ITokenSecretContainingMessage response)
        {
            string requestTokenFixed = response.Token.Replace(' ', '+');
            this.tokens.Add(requestTokenFixed, new TokenInfo { ConsumerKey = request.ConsumerKey, Token = response.Token, Secret = response.TokenSecret });
            this.requestTokens.Add(requestTokenFixed, false);
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
            string requestTokenFixed = requestToken.Replace(' ', '+');
            return this.requestTokens[requestTokenFixed];
        }

        public void ExpireRequestTokenAndStoreNewAccessToken(string consumerKey, string requestToken, string accessToken, string accessTokenSecret)
        {
            string requestTokenFixed = requestToken.Replace(' ', '+');
            string accessTokenFixed = accessToken.Replace(' ', '+');

            this.requestTokens.Remove(requestTokenFixed);
            this.accessTokens.Add(accessTokenFixed, accessToken);
            this.tokens.Remove(requestTokenFixed);
            this.tokens.Add(accessTokenFixed, new TokenInfo { Token = accessToken, Secret = accessTokenSecret });
        }

        /// <summary>
        /// Classifies a token as a request token or an access token.
        /// </summary>
        /// <param name="token">The token to classify.</param>
        /// <returns>Request or Access token, or invalid if the token is not recognized.</returns>
        public TokenType GetTokenType(string token)
        {
            string tokenFixed = token.Replace(' ', '+');

            if (this.requestTokens.ContainsKey(tokenFixed))
            {
                return TokenType.RequestToken;
            }
            else if (this.accessTokens.ContainsKey(tokenFixed))
            {
                return TokenType.AccessToken;
            }
            else
            {
                return TokenType.InvalidToken;
            }
        }

        #endregion
    }
}
