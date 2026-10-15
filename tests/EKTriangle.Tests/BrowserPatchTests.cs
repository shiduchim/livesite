using System;
using System.Linq;
using System.Text;
using EKTriangle.Core;
using Xunit;

namespace EKTriangle.Tests
{
    public class BrowserPatchTests
    {
        [Fact]
        public void Original_bundle_is_recognised_with_both_toggle_icons()
        {
            var a = BrowserPatch.Analyze(Js.Original());
            Assert.Equal(BrowserState.Original, a.State);
            Assert.Equal("Go", a.CollapsedComponent);
            Assert.Equal("Xo", a.ExpandedComponent);
            Assert.True(a.TogglePairSeen);
            Assert.Equal(1, a.AddCount);
        }

        [Fact]
        public void Apply_replaces_exactly_the_two_toggle_icons_and_nothing_else()
        {
            string original = Js.Original();
            string patched = BrowserPatch.Apply(original);

            Assert.Contains("d:\"M6 4L10 8L6 12Z\",style:{fill:\"#555555\"}", patched);
            Assert.Contains("d:\"M4 6L12 6L8 10Z\",style:{fill:\"#555555\"}", patched);
            Assert.DoesNotContain(BrowserPatch.CollapsedOriginal, patched);
            Assert.DoesNotContain(BrowserPatch.ExpandedOriginal, patched);
            // Add button, multi-path icon, toggle usage and toolbar untouched.
            Assert.Contains(Js.AddDef, patched);
            Assert.Contains(Js.MultiPathDef, patched);
            Assert.Contains(Js.Toggle, patched);
            Assert.Contains(Js.Toolbar, patched);
            // Only the two path objects differ.
            string expected = original.Replace(Js.PlusDef, Js.PlusDef.Replace(BrowserPatch.CollapsedOriginal, BrowserPatch.CollapsedEk))
                                      .Replace(Js.MinusDef, Js.MinusDef.Replace(BrowserPatch.ExpandedOriginal, BrowserPatch.ExpandedEk));
            Assert.Equal(expected, patched);
            Assert.Equal(BrowserState.Patched, BrowserPatch.Analyze(patched).State);
        }

        [Fact]
        public void Patched_component_is_still_valid_react_svg()
        {
            string patched = BrowserPatch.Apply(Js.Original());
            Assert.Contains("Go=function(e){return o.createElement(\"svg\",Ho({width:16,height:16},e),qo||(qo=o.createElement(\"path\",{d:\"M6 4L10 8L6 12Z\",style:{fill:\"#555555\"}})))};", patched);
            Assert.Contains("Xo=function(e){return o.createElement(\"svg\",Yo({width:16,height:16},e),Ko||(Ko=o.createElement(\"path\",{d:\"M4 6L12 6L8 10Z\",style:{fill:\"#555555\"}})))};", patched);
        }

        [Fact]
        public void Reverse_restores_byte_identical_text()
        {
            string original = Js.Original(7);
            Assert.Equal(original, BrowserPatch.Reverse(BrowserPatch.Apply(original)));
        }

        [Fact]
        public void Decode_round_trips_bom_and_non_ascii()
        {
            byte[] bytes = Js.Bytes(Js.Original(), bom: true);
            var d = BrowserPatch.Decode(bytes);
            Assert.True(d.HasBom);
            Assert.Equal(bytes, BrowserPatch.Encode(d));
            var p = BrowserPatch.Encode(new BrowserPatch.Decoded { HasBom = true, Text = BrowserPatch.Apply(d.Text) });
            Assert.Equal(0xEF, p[0]);
        }

        [Fact]
        public void Invalid_utf8_is_rejected()
        {
            byte[] bytes = Js.Bytes(Js.Original());
            bytes[5000] = 0xFF;
            Assert.ThrowsAny<Exception>(() => BrowserPatch.Decode(bytes));
        }

        [Theory]
        [InlineData("missing-minus")]
        [InlineData("missing-plus")]
        [InlineData("duplicate-plus")]
        [InlineData("duplicate-minus")]
        [InlineData("minus-in-multipath-svg")]
        [InlineData("plus-24px")]
        [InlineData("plus-extra-props")]
        [InlineData("plus-not-in-svg")]
        [InlineData("only-add-button")]
        [InlineData("mixed-ek-and-original")]
        [InlineData("altered-ek")]
        public void Unknown_or_ambiguous_bundles_are_never_patched(string kind)
        {
            string text = kind switch
            {
                "missing-minus" => Js.Original(minus: ""),
                "missing-plus" => Js.Original(plus: ""),
                "duplicate-plus" => Js.Original(extra: Js.PlusDef.Replace("Go=", "Ga=")),
                "duplicate-minus" => Js.Original(extra: Js.MinusDef.Replace("Xo=", "Xa=")),
                "minus-in-multipath-svg" => Js.Original(minus: "var Ko,Xo=function(e){return o.createElement(\"svg\",Yo({width:16,height:16},e),o.createElement(\"path\",{d:\"M0 0h1v1H0z\"}),Ko||(Ko=o.createElement(\"path\",{d:\"M4 7h8v2H4z\"})))};"),
                "plus-24px" => Js.Original(plus: Js.PlusDef.Replace("width:16,height:16", "width:24,height:24")),
                "plus-extra-props" => Js.Original(plus: Js.PlusDef.Replace("h3z\"}", "h3z\",fillRule:\"evenodd\"}")),
                "plus-not-in-svg" => Js.Original(plus: "var q={icon:{d:\"M7 7V4h2v3h3v2H9v3H7V9H4V7h3z\"}};"),
                "only-add-button" => Js.Original(plus: "", minus: ""),
                "mixed-ek-and-original" => Js.Original(extra: "var x={d:\"M6 4L10 8L6 12Z\",style:{fill:\"#555555\"}};"),
                "altered-ek" => BrowserPatch.Apply(Js.Original()).Replace("M4 6L12 6L8 10Z\",style:{fill:\"#555555\"}", "M4 6L12 6L8 10Z\",style:{fill:\"#ff0000\"}"),
                _ => throw new ArgumentException(kind),
            };
            var a = BrowserPatch.Analyze(text);
            Assert.Equal(BrowserState.Unsupported, a.State);
            Assert.False(a.CanApply);
            Assert.False(string.IsNullOrEmpty(a.Reason));
            Assert.ThrowsAny<InvalidOperationException>(() => BrowserPatch.Apply(text));
            Assert.ThrowsAny<InvalidOperationException>(() => BrowserPatch.Reverse(text));
        }

        [Fact]
        public void Legacy_T_symbol_is_recognised_and_converted_to_original()
        {
            string original = Js.Original();
            string t = original.Replace(BrowserPatch.CollapsedOriginal, BrowserPatch.LegacyT);
            Assert.Equal(BrowserState.LegacyT, BrowserPatch.Analyze(t).State);
            Assert.Equal(original, BrowserPatch.FromLegacyT(t));
        }

        [Fact]
        public void Different_minifier_names_are_supported_structurally()
        {
            string text = Js.Original()
                .Replace("Go=function", "nR=function").Replace("createElement(Go,", "createElement(nR,")
                .Replace("Ho({width", "aR({width").Replace("qo||(qo=", "iR||(iR=");
            var a = BrowserPatch.Analyze(text);
            Assert.Equal(BrowserState.Original, a.State);
            Assert.Equal("nR", a.CollapsedComponent);
            Assert.Equal(text, BrowserPatch.Reverse(BrowserPatch.Apply(text)));
        }

        [Fact]
        public void Approved_hash_table_contains_the_user_verified_2024_build()
        {
            Assert.NotNull(KnownBuilds.Describe("9CFAC3DA7225DA91039D30FCA37EAC589575450527EE00BC1503236B78841A39"));
            Assert.Null(KnownBuilds.Describe(new string('0', 64)));
        }
    }
}
