using System.Runtime.CompilerServices;
using System.Text;

namespace XFiles.Tests
{
    /// <summary>
    /// Register the CodePages encoding provider so linked production code that
    /// calls Encoding.GetEncoding(1252) behaves as it does on UWP/.NET Native.
    /// </summary>
    internal static class EncodingSetup
    {
        [ModuleInitializer]
        internal static void RegisterProvider()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
    }
}
