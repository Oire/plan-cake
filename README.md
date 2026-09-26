# 🥞 PlanCake

Read Markdown files comfortably and leave notes right where they belong.

PlanCake is a Windows desktop application that shows a Markdown file as properly rendered HTML,
so a screen reader such as JAWS reads it with heading, list and table navigation instead of
spelling out the punctuation, and lets you leave notes on any paragraph, list item, heading,
table row or code block. The notes are written straight into the `.md` file, between a pair of
markers (`[usernote]` … `[/usernote]` by default), where an AI assistant or a colleague can pick
them up.

PlanCake is under development; the first release is not out yet.

## Why

Implementation plans written by AI coding assistants are long Markdown files, often 800 lines and
more. Reading them raw in a code editor with JAWS means hearing "hash hash hash" and "star star"
all day, with no way to jump by heading, list or table. Rendering the file fixes the reading.

Reviewing a plan also means commenting on it: "this step is wrong", "do this one first". A web
page viewed in the JAWS virtual cursor cannot tell anyone *where* you are, and counting line
numbers in the source by ear is no fun. PlanCake annotates **blocks** instead of lines: every
rendered block knows which source lines it came from, so pressing Enter on it is enough to put a
note in exactly the right place in the file. Claude's manual-review step then reads those notes
and acts on them.

## Requirements

- Windows 10 or 11, x64
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
  (preinstalled on Windows 11)

## Building

```powershell
dotnet restore
dotnet build
dotnet test
dotnet format            # --verify-no-changes is what CI runs
```

Translations are compiled separately, since `.mo` files are build output and are gitignored:

```powershell
./src/PlanCake/locale/scripts/Compile-Translations.ps1
```

See `src/PlanCake/locale/README.md` for the translation workflow and `CLAUDE.md` for the
conventions this repository follows.

## License

PlanCake is licensed under the [Apache License 2.0](LICENSE).
