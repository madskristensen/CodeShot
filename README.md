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
whitespace on the left. Turn on **Keep Original Indentation** under the
**Options** gear when the indentation is the point of the screenshot.

![Tool window](art/tool-window.png)

## Draw attention to the lines that matter

Click a line in the preview to highlight it, and click it again to remove the
highlight. As soon as one line is highlighted the rest are dimmed, so the
screenshot points at the code you are talking about instead of leaving the
reader to find it. **Clear Line Highlights** under the **Options** gear removes
them all, and picking a new selection in the editor starts over.

## Annotate and redact before sharing

Open **Annotate** on the tool window toolbar to draw a rectangle around an
expression or cover sensitive content with an opaque redaction block. Choose
**Eraser** and click an annotation to remove it, or use **Clear Annotations** to
start over. Annotations are part of the exported PNG and reset when the editor
selection changes so they cannot drift onto different code. When a redaction is
present, CodeShot does not place the unredacted plain text on the clipboard.

## Customize the screenshot before exporting

On the tool window toolbar:

- Pick any monospaced font installed on your machine from the font drop-down.
- Pick a font size, or type your own.
- Under the **Options** gear: show or hide line numbers, show or hide the title
  bar, and keep the original indentation. Line numbers start at 1 by default, so
  the snippet reads as a standalone sample instead of exposing where it came
  from in the file. Turn on **Real Line Numbers** to number the lines from their
  position in the file instead, which is what you want when pointing at a
  specific place in the code.

In **Tools > Options > CodeShot > General**, which is what the gear button
opens:

![Options](art/options.png)

- Set the **window title** to anything you like. `{fileName}`,
  `{fileNameWithoutExtension}`, `{filePath}`, `{extension}`, `{language}` and
  `{workspace}` are replaced with the values from the captured document.
- Choose the **window controls** drawn in the title bar: the Windows minimize,
  maximize and close glyphs, the three macOS dots, or none at all.
- Set the **corner radius** of the code window, and turn the **drop shadow** on
  or off to lift the window off the background.
- Colors follow the Visual Studio editor theme automatically, or pick a custom
  background color, or blend two colors into a **gradient** at any angle, or make
  the background fully transparent so the screenshot blends into a slide or a
  page. Transparency is preserved when saving to a PNG file.
- Set the **line height** as a multiple of the font size. The default of 1.45
  opens the lines up the way a code sample in an article is set, and 1 packs them
  as tightly as the font allows.
- Set the **padding** to control the space around the code window, or set it to
  0 to export the code window on its own. The drop shadow is sized from the
  padding, so it always fades out before the edge of the image instead of being
  cut off there.
- Pick an **export scale** to control the resolution of the exported image. The
  default of 2x produces a sharp result, and the output is identical on every
  machine because the scale is used instead of the monitor DPI.

All of these settings are remembered between sessions. Leave the font family
empty or the font size at 0 to follow the Text Editor font.

Opening **CodeShot** already copies the screenshot to the clipboard, so most
captures are one keystroke and a paste. Changing the selection only updates the
preview and leaves the clipboard alone.

Export again at any time:

- **Copy Image** to send the PNG straight to the clipboard. Turn on **Copy plain
  text with image** under **Tools > Options > CodeShot > General** to put the
  selected code on the clipboard as plain text as well. Apps that accept images
  still paste the screenshot, while editors and chat clients that prefer text
  receive code that can be copied, searched and read by a screen reader.
- **Save Image As** to write a `.png` file to disk. Set a **save folder** and a
  **file name** under **Tools > Options > CodeShot > General** to control where
  the file lands and what it is called. The name takes the same placeholders as
  the window title, plus `{date}` and `{time}`. Turn off **Ask where to save** to
  skip the dialog entirely and write straight into that folder, which keeps a
  dialog out of every capture. An existing file is never overwritten, so a number
  is added to the name instead.

While the CodeShot window has focus, **Ctrl+C** and **Ctrl+S** run those two
commands. The shortcuts are scoped to that window, so **Edit > Copy** and
**File > Save** behave as usual everywhere else. Rebind them under the
**CodeShot** scope in **Tools > Options > Environment > Keyboard**.

**Shift+Esc** closes the CodeShot window when it has focus. That is a built-in
Visual Studio shortcut that closes any active tool window, so it works the same
way here as it does everywhere else in the IDE.

## Fits naturally in Visual Studio

CodeShot integrates as a standard extension command:

- **Tools > CodeShot**
- **Ctrl+Shift+P** from anywhere in the IDE

The tool window blends with the active Visual Studio theme and is designed for a
fast capture workflow while you code.

## Getting started

1. Install the extension from the [Visual Studio Marketplace][marketplace].
2. Open a code file and select the lines you want to capture.
3. Open **CodeShot** from the **Tools** menu or press **Ctrl+Shift+P**. The
   screenshot is on the clipboard, ready to paste.
4. Adjust the look, then press **Ctrl+C** to copy it again or **Ctrl+S** to save
   it as a `.png` file.

## Credits

CodeShot was inspired by the excellent
[CodeSnap](https://marketplace.visualstudio.com/items?itemName=adpyke.codesnap)
extension.

## Get involved

Found a bug or have an idea? Open an issue or pull request on the
[GitHub repo][repo].