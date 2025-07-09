# XboxKit

**XboxKit** is a multi-purpose utility for working with Xbox/Xbox360 DVD image formats. It supports extracting/combining Redump ISOs, XISO game images, video ISO partitions, random filler padding data, and the XGD3 system update file.

An example use case is creating a smaller, more compressible XISO that is usable by emulators such as Xemu and Xenia, with the ability to losslessly recreate the redump ISO:

`./xboxkit.exe -a game.iso`

Outputs:
- game.xiso (Useable by emulators, smaller, and compresses well)
- game.video.iso (Video partition, shared by similar discs)
- game.filler (Random padding filler data, needed for lossless conversion)
- game.seed (Initial seed used to generate early XGD1 disc random filler data)
- su20076000_00000000 (System update file for XGD3 only, shared by similar discs)

Losslessly converting back to the original redump ISO:
`./xboxkit.exe game.xiso`

If you have renamed the output files, you can explicitly give the paths for rebuilding the redump ISO:
`./xboxkit.exe game.xiso example.video.iso example.filler su20076000_00000000`
(replace example.filler with example.seed if applicable)

If you only want to retain the playable XISO from a redump ISO, then you can instead run:
`./xboxkit.exe -twx game.iso`
which will only output a trimmed, wiped, playable XISO.

### Help text

```
Usage: xboxkit.exe [options] <input.iso> [video.iso] [filler_data] [system_update_file]

Rebuild mode: Combine input files (no flags)
Extract mode: Use flags (optional paths are used for output file names)
-a, --all        Perform all operations on the input ISO
-q, --quiet      Don't print INFO messages to console
-r, --random     Extracts random filler data to a separate file
-s, --seed       Extracts RNG seed used for XGD1 filler
-t, --trim       Trims end of XISO (game partition)
-u, --update     Extracts update file from video ISO (XGD3 only)
-v, --video      Extracts video ISO (video partition)
-w, --wipe       Wipes filler data in XISO
-x, --xiso       Extracts XISO (game partition)
```
