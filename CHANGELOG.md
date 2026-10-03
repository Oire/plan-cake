# Changelog

All notable changes to PlanCake are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions have four parts, `X.Y.Z.N`,
set by GitVersion and never written by hand: a `vX.Y.Z` tag opens a development cycle, `N` counts
the commits since it, and a release is the commit tagged `vX.Y.Z.N`, with the version it builds
as. Until that tag exists, the changes wait under Unreleased. The release notes shown in the
update window come from `changelogs/<X.Y.Z>.md`.

## [Unreleased]

The first release, 1.0.0. PlanCake lets you read Markdown files comfortably and leave notes
right where they belong. It was made for the long implementation plans that AI coding assistants
write, and works with any Markdown file. It runs as a window, or without one from the command
line.

### Added

- **Read Markdown as a document**: real headings, lists, tables and code blocks instead of hash
  signs and asterisks, with GitHub-style tables, task lists, footnotes and more. Zoom from 50 to
  300 percent.
- **Leave notes on any block**: click a paragraph, heading, list item, table row or code block,
  or press Enter on it, and type a note. PlanCake writes it straight into the `.md` file, right
  after that block, between a pair of markers (`[usernote]` … `[/usernote]` by default). There
  is nothing to save and no side file.
- **Notes in the document and in a list**: notes show after their blocks, with the Markdown in
  them formatted, and in a notes list beside the document. Edit, delete, delete all, jump from
  note to note with F9 and Shift+F9, and undo and redo everything PlanCake writes.
- **Check off tasks**: task-list checkboxes toggle from the document and write `[x]` or `[ ]` to
  the file, after a question that can be turned off.
- **Open files every way**: from the command line, the Open dialog, drag and drop, the clipboard
  (a copied file or path), and a link, which is downloaded to the Downloads folder (GitHub file
  links work as they are). Links between Markdown files open in the same window, with Back and
  Forward.
- **One window per file**: opening a file that is already open brings its window to the front,
  so two windows never write into the same file.
- **Follows changes on disk**: when something else changes the file, PlanCake reloads it and
  keeps your reading position, or asks first if you prefer. A note is never written over a
  change you have not seen.
- **Safe with older files**: a file in a legacy encoding opens read-only unless you let PlanCake
  convert it to UTF-8, and a file whose encoding cannot be recognized is never changed.
- **Command line** for scripts and AI assistants: `plancake list` (text or JSON), `check` (exit
  code 3 while notes remain), `clear` and `export` to a standalone HTML page that carries its
  local pictures inside it, with other markers for a single run.
- **Keyboard, mouse and screen readers alike**: every action works from the keyboard alone and
  with the mouse, block-to-block movement with Alt+Shift+Down and Up Arrow, and status messages
  that screen readers speak wherever the focus is. Keyboard shortcuts are listed in the Help
  menu.
- **Settings** for the interface and document languages, the note markers, what Enter and a
  click do, confirmations, reloading and update checks, applied without a restart.
- **Six languages**: English, Russian, Ukrainian, French, Hebrew (with a right-to-left
  interface) and German, for the interface, the user manual (F1) and the installer. The
  document language is set apart from the interface language.
- **Automatic updates**, signed, with adjustable frequency.
- **Installer, portable zip and winget** (`Oire.PlanCake`). The installer puts `plancake` on the
  `PATH` and installs the .NET 10 Desktop Runtime and the WebView2 Runtime when missing.
- **Licenses**: PlanCake's license and the notices of the components it includes ship with it,
  and **Licenses** in About PlanCake opens them.

<!-- When the release is tagged, rename [Unreleased] to [1.0.0.N] - YYYY-MM-DD and start a new
     empty [Unreleased] above it. Then link [1.0.0.N] to
     https://github.com/Oire/plan-cake/releases/tag/v1.0.0.N and [Unreleased] to
     https://github.com/Oire/plan-cake/compare/v1.0.0.N...master (see Releasing in CLAUDE.md). -->
[Unreleased]: https://github.com/Oire/plan-cake/commits/master
