namespace XboxKit
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Options? opts = Helpers.ParseArgs(args);
            if (opts == null)
                return;

            if (!Helpers.ResolvePaths(opts))
                return;

            if (opts.RedumpIsoType >= 0)
            {
                ExtractRedump.Run(opts);
            }
            else if (opts.VideoIsoType >= 0)
            {
                ExtractVideo.Run(opts);
            }
            else
            {
                ProcessXISO.Run(opts);
            }
        }
    }
}
