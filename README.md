# XboxKit

**XboxKit** is a simple utility for working with Xbox/Xbox360 DVD image formats. It supports conversion between Redump ISOs and XISO game images, and optionally creates the auxiliary video ISOs and system update files.


## Mode 1: Redump → XISO (+ Video ISO + System Update)

Converts a Redump-format ISO into its component parts: game (XISO), video partition, and optionally separate system update (XGD3 only).

#### Standard Use

`xboxkit.exe input.iso`

Outputs:
- input.xiso.iso
- input.video.iso

#### Skip Video ISO

`xboxkit.exe input.iso --skip` will create input.xiso.iso only

#### Only Video ISO

`xboxkit.exe input.iso --video-only` will create input.video.iso only

#### System Update extraction (XGD3 only)

`xboxkit.exe input.iso --unpack`

Outputs:
- input.xiso.iso
- input.video.iso
- su20076000_00000000 (system update file)

Also zeroes the system update file inside the video partition

#### User-Defined output names

`xboxkit.exe --unpack input.iso video.iso system_update`

Outputs:
- input.xiso.iso
- video.iso
- system_update

## Mode 2: XISO + Video ISO (+ System Update) → Redump

Combines game (XISO) and video partitions to build a redump ISO.

Optionally uses a system update file (typically named su20076000_00000000) for XGD3 ISOs.

### Standard Use

`xboxkit.exe input.iso video.iso` will create input.redump.iso

### System Update writing (XGD3 only)

`xboxkit.exe input.iso video.iso su20076000_00000000` will create input.redump.iso

## Mode 3: Video ISO → System Update

Extracts system update file from a video partition.

`xboxkit.exe video.iso` will create su20076000_00000000

**WARNING**: Also zeroes the system update file inside the input video partition
