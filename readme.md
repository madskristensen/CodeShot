[marketplace]: <https://marketplace.visualstudio.com/items?itemName=madskristensen.CodeShot>
[vsixgallery]: <https://www.vsixgallery.com/extension/CodeShot.06cf14ac-c16c-4c61-9e83-2edd8692648c>
[repo]: <https://github.com/madskristensen/CodeShot>

# CodeShot for Visual Studio

[![Build](https://github.com/madskristensen/CodeShot/actions/workflows/build.yaml/badge.svg)](https://github.com/madskristensen/CodeShot/actions/workflows/build.yaml)
[![Install from VSIX Gallery](https://www.vsixgallery.com/badge/CodeShot.06cf14ac-c16c-4c61-9e83-2edd8692648c.png)][vsixgallery]
[![GitHub Sponsors](https://img.shields.io/github/sponsors/madskristensen)](https://github.com/sponsors/madskristensen)

Download this extension from the [Visual Studio Marketplace][marketplace]
or get the latest CI build from [Open VSIX Gallery][vsixgallery].

Turn the current editor selection into a polished PNG screenshot without leaving
Visual Studio.

----------------------------------------------

**CodeShot captures exactly what you selected and lets you copy or save it as an
image in seconds.**

## Capture code without extra tools

Select code in the editor, open **CodeShot**, and click **Refresh**. The preview
updates from your current selection and keeps syntax coloring when available.
The common leading indentation is removed automatically, so a deeply nested
selection is captured without the extra whitespace on the left.

![Tool window](art/tool-window.png)

## Customize the screenshot before exporting

You can quickly control the final image:

- Show or hide line numbers.
- Show or hide the title bar.
- Keep Visual Studio theme-aware colors in the preview.

Then export with one click:

- **Copy PNG** to send directly to clipboard.
- **Save PNG** to write a `.png` file to disk.

## Fits naturally in Visual Studio

CodeShot integrates as a standard extension command:

- **Tools > CodeShot**
- Editor context menu in code windows

The tool window blends with the active Visual Studio theme and is designed for a
fast capture workflow while you code.

## Getting started

1. Install the extension from the [Visual Studio Marketplace][marketplace].
2. Open a code file and select the lines you want to capture.
3. Open **CodeShot** from the **Tools** menu or editor context menu.
4. Click **Refresh**, then **Copy PNG** or **Save PNG**.

## Credits

CodeShot was inspired by the excellent
[CodeSnap](https://marketplace.visualstudio.com/items?itemName=adpyke.codesnap)
extension.

## Get involved

Found a bug or have an idea? Open an issue or pull request on the
[GitHub repo][repo].