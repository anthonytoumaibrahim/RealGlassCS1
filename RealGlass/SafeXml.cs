using System;
using System.IO;
using System.Xml;

namespace RealGlass
{
    /// <summary>
    /// Reads a text a player may have edited by hand as XML, and only as XML: no DTD (so no entity
    /// can expand into gigabytes), nothing fetched from anywhere, comments and processing
    /// instructions left out. Never throws: a text that is not well-formed is an answer, not an
    /// exception. Pure, for tools/test-settings.ps1. Both of the mod's files go through it
    /// (RealGlass.xml, RealGlassAssets.xml).
    /// </summary>
    public static class SafeXml
    {
        /// <summary>The text's outermost element; false, and why (the parser's line and position), when it is not well-formed XML.</summary>
        public static bool TryLoad(string text, out XmlElement root, out string error)
        {
            root = null;
            error = null;

            try
            {
                var settings = new XmlReaderSettings();
                settings.ProhibitDtd = true;
                settings.XmlResolver = null;
                settings.IgnoreComments = true;
                settings.IgnoreProcessingInstructions = true;

                var document = new XmlDocument();
                document.XmlResolver = null;
                using (var reader = XmlReader.Create(new StringReader(text ?? string.Empty), settings))
                    document.Load(reader);

                root = document.DocumentElement;
                if (root == null)
                {
                    error = "there is no element in it";
                    return false;
                }

                return true;
            }
            catch (XmlException e)
            {
                error = e.Message;
            }
            catch (Exception e)
            {
                // Not expected from a string; whatever it is, the text cannot be read.
                error = e.GetType().Name + ": " + e.Message;
            }

            root = null;
            return false;
        }
    }
}
