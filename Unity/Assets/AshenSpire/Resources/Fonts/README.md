# Redistributable display and symbol fonts

Cinzel Regular is the display family named by the original HTML stylesheet.
Source: https://github.com/NDISCOVER/Cinzel/tree/dd598495b0fb2ad84270d5cc75d642d2f1e8eabf/fonts/ttf
Original filename: Cinzel-Regular.ttf. License: Cinzel-OFL.txt (SIL OFL 1.1).
The font file is unmodified.

Noto Sans Symbols 2 supplies the original authored Unicode card glyphs.
Source: https://github.com/google/fonts/tree/7b6724ac7ececc713e9ba93af309f7520c9a80a3/ofl/notosanssymbols2
Original filename: NotoSansSymbols2-Regular.ttf. License: NotoSansSymbols2-OFL.txt.
The font file is unmodified. Neither font is fetched at player runtime.

Noto Sans Symbols Regular supplements the crossed-swords and other BMP glyphs
absent from Symbols 2. Its unmodified static TTF and accompanying OFL license
come from https://github.com/notofonts/noto-fonts/tree/main/hinted/ttf/NotoSansSymbols.


## Authored glyph coverage (build 14)

`GlyphFonts.json` maps all 110 unique authored card icons and 25 tag glyphs
(120 distinct strings total) to a bundled font whose Unicode cmap contains every
non-variation-selector codepoint in that string. Coverage includes supplementary
codepoints; runtime selection does not truncate Unicode to a C# char. Tag glyphs
use their own label so the tag name keeps its ordinary Latin font.

Noto Sans Regular supplies the dagger, Noto Sans Mono supplies the authored
wavy arrow and diagonal line, and Noto Emoji supplies missing emoji symbols.
Noto Sans/Mono are unmodified static upstream fonts, licensed by NotoSans-OFL.txt.
Noto Emoji is a static weight-400 instance of Google's current monochrome
variable font (NotoEmoji-OFL.txt); its output contains glyf outlines and no
variable or color-font tables. Source blob IDs, hashes and conversion version
are in GlyphFontProvenance.json. All shipped font hashes are in checksums.json.

When content icons/tags change, enumerate cards[].icon and tags[].glyph from
Original/content.json, select a font containing all Unicode scalar values in
its cmap (ignore FE0E/FE0F and ZWJ selectors), and regenerate GlyphFonts.json.
Use this priority: NotoSansSymbols, NotoSansSymbols2, NotoSans, NotoSansMono,
NotoEmoji. Preserve exact authored strings as keys. Fail the coverage check if
any string is uncovered. Runtime keeps unfamiliar text verbatim; the coverage
map must be updated to guarantee its font choice. Cmap coverage alone does not
prove Unity renders each glyph; compiled screenshots remain a separate check.
