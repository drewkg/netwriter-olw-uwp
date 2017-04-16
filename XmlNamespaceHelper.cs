using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace BlogWriter.OpenLiveWriter
{
    public static class XmlNamespaceHelper
    {
        public static string ToNSMethodFormat(this XmlNamespaceManager manager)
        {
            var namespaces = new List<string>();
            foreach (var ns in manager.GetNamespacesInScope(XmlNamespaceScope.ExcludeXml))
            {
                namespaces.Add($"xmlns:{ns.Key}='{ns.Value}'");
            }
            return string.Join(" ", namespaces);
        }
    }
}
