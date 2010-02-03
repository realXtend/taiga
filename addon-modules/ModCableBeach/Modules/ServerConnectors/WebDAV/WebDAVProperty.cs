using System;
using System.Collections.Generic;
using System.Text;

namespace ModCableBeach
{
    public class WebDAVProperty
    {
        string name;
        string nspace;
        string value;
        int resourceId;
        int id;

        public virtual int Id
        {
            get { return id; }
            set { id = value; }
        }

        public virtual int ResourceId
        {
            get { return resourceId; }
            set { resourceId = value; }
        }

        public virtual string Namespace
        {
            get { return nspace; }
            set { nspace = value; }
        }

        public virtual string Name
        {
            get { return name; }
            set { name = value; }
        }

        public virtual string Value
        {
            get { return value; }
            set { this.value = value; }
        }

        public WebDAVProperty() { }

        public WebDAVProperty(string name)
        {
            this.name = name;
            value = String.Empty;
            nspace = String.Empty;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WebDAVProperty"/> class.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="nspace">The namespace.</param>
        public WebDAVProperty(string name, string nspace)
        {
            this.name = name;
            this.value = String.Empty;
            this.nspace = nspace;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WebDAVProperty"/> class.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="nspace">The namespace.</param>
        /// <param name="value">The value.</param>
        public WebDAVProperty(string name, string nspace, string value)
        {
            this.name = name;
            this.value = value;
            this.nspace = nspace;
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;
            if (obj is WebDAVProperty)
            {
                WebDAVProperty wobj = (WebDAVProperty)obj;
                if (wobj.Namespace == this.Namespace &&
                    wobj.Name == this.Name &&
                    wobj.Value == this.Value)
                {
                    return true;
                }
            }
            return false;
        }

        public override int GetHashCode()
        {
            return nspace.GetHashCode() ^ name.GetHashCode() ^ value.GetHashCode();
        }

        public override string ToString()
        {
            return nspace + ":" + name + ":" + value;
        }
    }
}
