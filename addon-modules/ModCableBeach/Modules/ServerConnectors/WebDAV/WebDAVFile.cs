/* 
 * Copyright (c) Intel Corporation
 * All rights reserved.
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions
 * are met:
 *
 * -- Redistributions of source code must retain the above copyright
 *    notice, this list of conditions and the following disclaimer.
 * -- Redistributions in binary form must reproduce the above copyright
 *    notice, this list of conditions and the following disclaimer in the
 *    documentation and/or other materials provided with the distribution.
 * -- Neither the name of the Intel Corporation nor the names of its
 *    contributors may be used to endorse or promote products derived from
 *    this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
 * ``AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
 * LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A
 * PARTICULAR PURPOSE ARE DISCLAIMED.  IN NO EVENT SHALL THE INTEL OR ITS
 * CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
 * EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
 * PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
 * PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
 * LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
 * NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;

namespace ModCableBeach
{
    public class WebDAVFile : IWebDAVResource
    {
        private int id = 0;
        /// <summary>
        ///  Id for the NHibernate storage
        /// </summary>
        public virtual int Id
        {
            get { return id; }
            set { id = value; }
        }
        string path;
        string contentType;
        int contentLength;
        DateTime creationDate;
        DateTime lastModifiedDate;
        DateTime lastAccessedDate;
        bool isHidden;
        bool isReadOnly;
        string displayName = String.Empty;
        string contentLanguage = "en";
        string resourceType = String.Empty;
        IList<WebDAVProperty> customProperties = new List<WebDAVProperty>();

        public virtual string Path { get { return path; } set { path = value; } }
        public virtual string ContentType { get { return contentType; } set { contentType = value; } }
        public virtual int ContentLength { get { return contentLength; } set { contentLength = value; } }
        public virtual DateTime CreationDate { get { return creationDate; } set { creationDate = value; } }
        public virtual DateTime LastModifiedDate { get { return lastModifiedDate; } set { lastModifiedDate = value; } }
        public virtual DateTime LastAccessedDate { get { return lastAccessedDate; } set { lastAccessedDate = value; } }
        public virtual bool IsFolder { get { return false; } }
        public virtual bool IsHidden { get { return isHidden; } set { isHidden = value; } }
        public virtual bool IsReadOnly { get { return false; } set { isReadOnly = value; } }
        public virtual string DisplayName { get { return displayName; } set { displayName = value; } }
        public virtual string ContentLanguage { get { return contentLanguage; } set { contentLanguage = value; } }
        public virtual string ResourceType { get { return resourceType; } set { resourceType = value; } }
        public virtual IList<WebDAVProperty> CustomProperties
        {
            get { return customProperties; }
            set { customProperties = value; }
        }

        public WebDAVFile() { }

        public WebDAVFile(string path, string contentType, int contentLength, DateTime creationDate, DateTime lastModifiedDate,
            DateTime lastAccessedDate, bool isHidden, bool isReadOnly)
        {
            this.path = path;
            this.contentType = contentType;
            this.contentLength = contentLength;
            this.creationDate = creationDate;
            this.lastModifiedDate = lastModifiedDate;
            this.lastAccessedDate = lastAccessedDate;
            this.isHidden = isHidden;
            this.isReadOnly = isReadOnly;
        }

        public virtual void AddProperty(WebDAVProperty property)
        {
            property.ResourceId = this.id;
            customProperties.Add(property);
        }
    }
}
