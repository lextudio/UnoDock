// Loads the embedded vstheme.gz byte arrays from the assembly manifest.

using System.IO;
using System.Reflection;

namespace UnoDock.Themes.VS2013
{
    internal static class VsThemeResources
    {
        private static readonly Assembly _asm = typeof(VsThemeResources).Assembly;
        private const string Prefix = "UnoDock.Themes.VS2013.Resources.";

        public static byte[] Blue  => Load("vs2013blue.vstheme.gz");
        public static byte[] Dark  => Load("vs2013dark.vstheme.gz");
        public static byte[] Light => Load("vs2013light.vstheme.gz");

        private static byte[] Load(string name)
        {
            using var stream = _asm.GetManifestResourceStream(Prefix + name);
            if (stream == null)
                throw new FileNotFoundException($"Embedded resource '{Prefix + name}' not found.");
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
