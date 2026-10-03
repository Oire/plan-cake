# Edge cases

Setext heading
==============

A paragraph that shows the markers as code: `[usernote]`, `[/usernote]` and `!USERNOTE!`.

> Quote line one
> lazy continuation
lazy line

> - quoted item
>   - nested quoted

1. First
   ```
   [usernote]a marker in a fence in a list item[/usernote]
   ```
2. Second
	- tab nested item
		- deeper tab

| a | b |
|---|---|
| 1 | `[usernote]` |
| 3 | 4 |

<div>
raw html block
</div>

Term with footnote[^1].

[^1]: The footnote text.

- [ ] task
  - [x] done child

Indented code follows:

    indented code
    [usernote]a marker in indented code[/usernote]
    !USERNOTE! another one
    last line of the code

~~~~text
```
[usernote]inside a longer tilde fence[/usernote]
```
~~~~

* * *

Last paragraph.
