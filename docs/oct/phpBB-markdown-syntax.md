# opencheattables.com phpBB Markdown Reference

> Source-of-truth Markdown syntax supported by the phpBB markdown
> extension running on opencheattables.com. Use this as the input
> dialect when writing forum posts for that site — features that
> aren't listed here (e.g. embedded HTML, footnotes, definition
> lists) may not render.
>
> The output in this folder (`features-highlights.md`) is written
> against this dialect.

## Text emphasis

### Bold

Wrap in `**` or `__`:

```
**Hello**
__Hello__
```

### Italic

Wrap in `*` or `_`:

```
*Great!*
_Great!_
```

### Strikethrough

Wrap in `~~`:

```
~~Good morning~~
```

### Subscript

Wrap in `~`:

```
H~2~O
```

### Superscript

Prefix with `^`:

```
2^n
```

## Headers

Lead with 1–6 `#` + space. More `#` = smaller text:

```
# H1
## H2
### H3
#### H4
##### H5
###### H6
```

## Quoting and fixed-width text

### Quote in replies

Lead with `>` (optional space):

```
> Quoted text
```

### Code block

Fence with ` ``` ` or `~~~`, or indent every line with 4 spaces.
Optionally annotate with a language tag on the opening fence:

```ruby
puts "Hello #{user}!"
```

### Inline code

Wrap in `` ` `` or `` `` ``:

```
`<div>` tag
``<div>`` tag
```

## Tables

A header row + a divider row of `-` (optionally with `:` on the left,
both sides, or right for left/center/right alignment), then any number
of body rows — all `|`-delimited:

```
| Left | Center | Right |
|:-----|:------:|------:|
|   x  |    x   |   x   |
```

Pipes at the start/end are optional:

```
Left|Center|Right
:-|:-:|-:
x|x|x
```

## Spoilers

### Block spoiler

Lead with `>!` (optional space). Subsequent lines can start with `>`:

```
>! Spoiler text
> Another line
```

### Inline spoiler

Reddit style — wrap in `>!` and `!<`:

```
This is a Reddit-style >!spoiler!<.
```

Discord style — wrap in `||`:

```
This is a Discord-style ||spoiler||.
```

## Lists

### Unordered

`*`, `-`, or `+` followed by a space. Indent **4 spaces** (or a tab)
to nest:

```
- Element
    - Subelement
- Element
```

### Ordered

Digit + dot + space:

```
1. Element
    1. Subelement
2. Element
```

### Task list

`*`/`-`/`+`, space, `[x]` (checked) or `[ ]` (unchecked), space:

```
- [x] Element
    - [x] Subelement
- [ ] Element
```

## Links

`[text](url)`:

```
[Link text](http://example.org)
```

## Images

`![alt](url)`:

```
![phpBB](https://www.phpbb.com/assets/images/images/logos/blue/160x52.png)
```

## Extras

### Horizontal rule

At least 3 `*`, `-`, or `_` (optional spaces between):

```
***
* * *
---
- - -
___
_ _ _
```
