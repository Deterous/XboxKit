using System.Collections.Generic;

namespace XboxKit
{
    internal enum Mode
    {
        ExtractRedump,
        ExtractVideo,
        ProcessXISO,
        RebuildISO
    }

    internal class Options
    {
        public Mode Mode;
        public bool Help;
        public bool ExtractXRD;
        public bool AssumeNo;
        public bool OutputFiles;
        public bool ExtractSkeleton;
        public bool Quiet;
        public bool ExtractFiller;
        public bool ExtractSeed;
        public bool TrimXISO;
        public bool ExtractUpdate;
        public bool ExtractVideo;
        public bool WipeXISO;
        public bool ExtractXISO;
        public bool AssumeYes;
        public bool ExtractZAR;
        public List<string> FilePaths = new();

        // Resolved paths
        public string IsoPath = "";
        public string RedumpPath = "";
        public string XrdPath = "";
        public string OutputPath = "";
        public string SkeletonPath = "";
        public string FillerPath = "";
        public string SeedPath = "";
        public string SectorsTXTPath = "";
        public string UpdatePath = "";
        public string VideoPath = "";
        public string XisoPath = "";
        public string ZarPath = "";

        // Derived from input file
        public long IsoSize;
        public int RedumpIsoType = -1;
        public int VideoIsoType = -1;
        public int XisoType = -1;
    }
}
