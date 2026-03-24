# XboxKit

**XboxKit** losslessly converts between Xbox & Xbox 360 DVD image file formats. It supports Redump ISOs, XISO game images, video ISO partitions, random filler padding data, XGD1 filler data seeds, XGD3 system update files, XISO skeletons, and ZAR files.

## Help text

```
Usage: xboxkit.exe [options] <input.iso> [files]

Rebuild mode: Combine input files (no options)
Extract mode: Use one or more options
-a, --all        Perform all operations (-rstuvwx) on the input ISO
-q, --quiet      Don't print INFO messages to console
-r, --random     Extracts random filler data to a separate file
-s, --seed       Extracts RNG seed used for XGD1 filler
-t, --trim       Trims end of XISO (game partition)
-u, --update     Extracts update file from video ISO (XGD3 only)
-v, --video      Extracts video ISO (video partition)
-w, --wipe       Wipes filler data in XISO
-x, --xiso       Extracts XISO (game partition)
-y, --skelly     Extracts XISO skeleton (game partition with zeroed files)
-z, --zar        Converts XISO to zar (zstd compressed archive of game files)
```

**Note**: Extracting the system update (su20076000_00000000) is only useful for XGD3 discs as deduplication of the video ISO is possible for XGD1/XGD2. When extracting the update, XboxKit zeroes it file within the video ISO so that it becomes highly compressible (deduplication of su20076000_00000000 is then possible across multiple XGD3 disc images). XboxKit will ignore the --update option when used with XGD1/XGD2.

## Examples

For lossless conversion from a redump ISO to an XISO, run:
`./xboxkit.exe -a game.iso`

Outputs:
- game.xiso (Useable by emulators, smaller, and compresses well)
- game.video.iso (Video partition, shared by similar discs with the same "wave")
- game.filler (Random padding filler data, needed for lossless conversion)
- game.seed (Initial seed used to generate early XGD1 disc random filler data)
- su20076000_00000000 (System update file for XGD3 only, shared by similar discs)

Losslessly converting back to the original redump ISO:
`./xboxkit.exe game.xiso`
(requires all the original output files).

Losslessly converting from a redump ISO to a ZAR file:
`./xboxkit.exe -ayz game.iso`

Additionally outputs:
- game.xiso.skeleton (XISO with all game files zeroed)
- game.zar (zstd compressed archive of game files)

Losslessly converting back to the original redump ISO:
`./xboxkit.exe game.zar`
(requires all the original output files).

If you have renamed the output files, you can explicitly give the paths for rebuilding the redump ISO:
`./xboxkit.exe game.xiso example.video.iso example.filler su20076000_00000000`
(replace example.filler with example.seed if applicable)

Only creating a playable XISO from a redump ISO:
`./xboxkit.exe -twx game.iso`
which will only output a trimmed, wiped, playable XISO (cannot convert back to redump ISO).

Only creating a playable ZAR file from a redump ISO:
`./xboxkit.exe -z game.iso`
which will only output a zstd-compressed archive of game files (cannot convert back to redump ISO).

For more info on using the program, run `./xboxkit.exe --help`

---

# Technical Notes

XboxKit was developed as a tool for two-way lossless conversion between large collections of redump-style Xbox & Xbox 360 ISOs and compressed playable formats such as XISO and ZAR. This achieves the balance of archival quality and compressed playable formats, by storing the auxiliary data in sidecar files that can be managed and stored separately (with deduplication and compression). These sidecar files (such as the random filler data, skeleton, and game file hashes) do not contain any copyright data, and can be safely shared publicly to allow people with their own backups to confirm the files are not corrupted and repair them to match redump hashes. XboxKit therefore makes it possible for someone with only a backup of the loose game files to rebuild to an redump ISO for archival purposes.

Xbox & Xbox 360 DVDs (commonly referred to as XGDs) are not physically different from other dual-layer DVDs. A few tweaks to the disc's data format hides the game partition from standard DVD drives, but [Redumper](https://github.com/superg/redumper) supports reading the full disc like any standard DVD (requires a disc drive with [OmniDrive](https://github.com/RibShark/OmniDrive) or [Kreon](http://wiki.redump.org/index.php?title=Optical_Disc_Drive_Compatibility:_Xbox_(original)_%26_Xbox_360) custom firmware).

The DVD's PFI[^1] sector indicates to the drive that the DVD's layerbreak[^2] is after a small "video partition". The Security Sector (SS)[^3] is what is read by Xbox disc drives and instead points to the game partition of the disc, with the true layerbreak value. Redump-style ISOs aim to preserve the entire disc by combining both the video and game partitions into a single ISO file (merging the PFI and SS descriptors). XISO files instead represent only the game partition pointed to by the SS (removing both the video partition and the middle zones between the partition on both layers). The XISO file uses the Xbox filesystem (commonly referred to as XDVDFS) that is not readable by Windows. Other programs such as [extract-xiso](https://github.com/xboxdev/extract-xiso) rewrite the xbox filesystem in a lossy manner in order to optimize for file size, while the XISO produced by XboxKit keeps the original filesystem intact.

[^1]: Physical Format Information, a sector in the lead-in describing the disc's data layout.
[^2]: Sector number at which the data switches to the 2nd layer.
[^3]: An XGD-specific sector in the lead-out of the DVD that follows the PFI spec, with other data in the reserved bytes.