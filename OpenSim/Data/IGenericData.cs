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
using OpenMetaverse;
using OpenSim.Framework;

namespace OpenSim.Data
{
    /// <summary>
    /// An interface for connecting to the generic key/value store
    /// </summary>
    public interface IGenericData
    {
        /// <summary>
        /// Attempts to retrieve a value from the generic data store
        /// </summary>
        /// <param name="scope">Scope of the request. Set this to a constant to
        /// limit the context of the request, or null for a global key request
        /// This parameter is case sensitive</param>
        /// <param name="key">The key to look up. This parameter is case
        /// sensitive</param>
        /// <returns>The string value corresponding to the requested scope and 
        /// key on success, otherwise null</returns>
        string Get(string scope, string key);

        /// <summary>
        /// Store a key/value pair in the generic store by creating a new entry
        /// or updating an existing entry
        /// </summary>
        /// <param name="scope">Scope of the request. Set this to a constant to
        /// limit the context of the store, or null for a global key store 
        /// This parameter is case sensitive</param>
        /// <param name="key">Key to store. This parameter is case sensitive</param>
        /// <param name="value">Value to store</param>
        /// <returns>True if existing data was updated, otherwise false if a
        /// new entry was created</returns>
        bool Store(string scope, string key, string value);

        /// <summary>
        /// Removes an entry from the generic key/value store
        /// </summary>
        /// <param name="scope">Scope of the request. Set this to a constant to
        /// limit the context of the delete, or null for a global key delete
        /// This parameter is case sensitive</param>
        /// <param name="key">Key to remove. This parameter is case sensitive</param>
        /// <returns>True if an entry was found and removed, otherwise false</returns>
        bool Remove(string scope, string key);
    }
}
