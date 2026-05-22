using System.IO;

namespace LibXGD
{
    public class ZArchive
    {
        // TODO: Implement ZArchive creation from game files
        public static bool CreateZAR(FileStream isoFS, long xisoOffset, string zarPath, bool quiet)
        {
            // Dummy: creates a 0-byte ZAR file
            using FileStream zarFS = new(zarPath, FileMode.Create, FileAccess.Write, FileShare.None);
            return true;
        }
    }
}
