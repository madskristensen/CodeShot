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

Select code in the editor and open **CodeShot**. The preview follows your
selection and keeps syntax coloring when available, and the toolbar **Refresh**
button pulls it in again at any time. The common leading indentation is removed
automatically, so a deeply nested selection is captured without the extra
whitespace on the left.

![Tool window](art/tool-window.png)

## Customize the screenshot before exporting

Everything lives on the tool window toolbar:

- Pick any monospaced font installed on your machine from the font drop-down.
- Pick a font size, or type your own.
- Under the **Options** gear: show or hide line numbers, and show or hide the
  title bar. Line numbers always start at 1, so the snippet reads as a
  standalone sample instead of exposing where it came from in the file.
- Colors follow the Visual Studio editor theme automatically.

All of these settings are remembered between sessions and can also be edited in
**Tools > Options > CodeShot > General**, which is what the gear button opens.
Leave the font family empty or the font size at 0 to follow the Text Editor
font.

Then export with one click:

- **Copy Image** to send the PNG straight to the clipboard.
- **Save Image As** to write a `.png` file to disk.

While the CodeShot window has focus, **Ctrl+C** and **Ctrl+S** run those two
commands. The shortcuts are scoped to that window, so **Edit > Copy** and
**File > Save** behave as usual everywhere else. Rebind them under the
**CodeShot** scope in **Tools > Options > Environment > Keyboard**.

## Fits naturally in Visual Studio

CodeShot integrates as a standard extension command:

- **Tools > CodeShot**
- **Ctrl+Shift+P** from anywhere in the IDE

The tool window blends with the active Visual Studio theme and is designed for a
fast capture workflow while you code.

## Getting started

1. Install the extension from the [Visual Studio Marketplace][marketplace].
2. Open a code file and select the lines you want to capture.
3. Open **CodeShot** from the **Tools** menu or press **Ctrl+Shift+P**.
4. Click **Copy Image** or **Save Image As** on the toolbar, or press **Ctrl+C** or **Ctrl+S**.

## Credits

CodeShot was inspired by the excellent
[CodeSnap](https://marketplace.visualstudio.com/items?itemName=adpyke.codesnap)
extension.

## Get involved

Found a bug or have an idea? Open an issue or pull request on the
[GitHub repo][repo].