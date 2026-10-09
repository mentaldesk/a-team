using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Color = Terminal.Gui.Drawing.Color;

namespace ATeam.Dashboard.Tests;

/// <summary>Each role's animation in Overseer, frame for frame as the stakeholder drew them on #404.</summary>
public class RoleAnimationTests
{
    public static TheoryData<string> Roles => ["dev", "lead", "customer"];

    [Theory]
    [InlineData("dev", "Decrypt", 140, 72, 56)]
    [InlineData("lead", "Thought to idea", 280, 10, 10)]
    [InlineData("customer", "Typing", 220, 14, 8)]
    public void Each_role_plays_its_own_animation_at_its_own_speed(string role, string name, int ms, int chip, int header)
    {
        var animation = RoleAnimation.For(role)!;

        Assert.Equal(name, animation.Name);
        Assert.Equal(TimeSpan.FromMilliseconds(ms), animation.Every);
        Assert.Equal(chip, animation.ChipFrames().Count);
        Assert.Equal(header, animation.HeaderFrames().Count);
        Assert.Equal(0, ms % (int)OverseerView.SpinEvery.TotalMilliseconds);
    }

    [Fact]
    public void A_role_with_no_animation_has_none() => Assert.Null(RoleAnimation.For("reviewer"));

    [Fact]
    public void Thought_to_idea_and_typing_are_the_frames_in_the_comment()
    {
        Assert.Equal(
            [".oO", ".oO", " oO", "  O", "  ✺", "  ✹", "  ✦", "  ✦", ". ✧", ".o·"],
            RoleAnimation.ThoughtToIdea.ChipFrames().Select(frame => frame.Text));
        Assert.Equal(
            ["011", "011", "011", "001", "002", "002", "002", "001", "001", "010"],
            RoleAnimation.ThoughtToIdea.ChipFrames().Select(frame => frame.Tones));
        Assert.Equal(".oOO✺✹✦✦✧·", string.Concat(RoleAnimation.ThoughtToIdea.HeaderFrames().Select(frame => frame.Text)));
        Assert.Equal("0111222110", string.Concat(RoleAnimation.ThoughtToIdea.HeaderFrames().Select(frame => frame.Tones)));
        Assert.Equal(
            ["⣤⣤⣤", "⠶⣤⣤", "⠛⣤⣤", "⠶⠶⣤", "⣤⠛⣤", "⣤⠶⠶", "⣤⣤⠛", "⣤⣤⠶", "⣤⣤⣤", "⣤⣤⣤", "⣤⣤⣤", "⣤⣤⣤", "⣤⣤⣤", "⣤⣤⣤"],
            RoleAnimation.Typing.ChipFrames().Select(frame => frame.Text));
        Assert.Equal(
            ["000", "100", "200", "110", "020", "011", "002", "001", "000", "000", "000", "000", "000", "000"],
            RoleAnimation.Typing.ChipFrames().Select(frame => frame.Tones));
        Assert.Equal("⣤⠶⠛⠶⣤⣤⣤⣤", string.Concat(RoleAnimation.Typing.HeaderFrames().Select(frame => frame.Text)));
        Assert.Equal("01210000", string.Concat(RoleAnimation.Typing.HeaderFrames().Select(frame => frame.Tones)));
    }

    [Fact]
    public void Decrypt_locks_into_the_same_four_words_every_cycle()
    {
        foreach (var cycle in Enumerable.Range(0, 3))
        {
            var frames = RoleAnimation.Decrypt.ChipFrames(cycle, salt: 404);
            for (var word = 0; word < 4; word++)
            {
                var run = frames.Skip(word * 18).Take(18).ToList();
                var locked = RoleAnimation.Words[word];
                Assert.Equal(Enumerable.Repeat("111", 5).Concat(["211", "211", "221", "221"]).Concat(Enumerable.Repeat("222", 4))
                    .Concat(Enumerable.Repeat("111", 5)), run.Select(frame => frame.Tones));
                Assert.All(run.Skip(5).Take(2), frame => Assert.StartsWith(locked[..1], frame.Text, StringComparison.Ordinal));
                Assert.All(run.Skip(7).Take(2), frame => Assert.StartsWith(locked[..2], frame.Text, StringComparison.Ordinal));
                Assert.All(run.Skip(9), frame => Assert.Equal(locked, frame.Text));
            }

            var header = RoleAnimation.Decrypt.HeaderFrames(cycle, salt: 1);
            Assert.Equal(">>>>>>>>>}}}}}}}}};;;;;;;;;#########",
                string.Concat(header.Where((_, at) => at % 14 >= 5).Select(frame => frame.Text)));
            Assert.Equal(string.Concat(Enumerable.Repeat("11111222211111", 4)), string.Concat(header.Select(frame => frame.Tones)));
        }
    }

    [Fact]
    public void Decrypt_scrambles_differently_each_cycle()
    {
        static string Scrambled(int cycle) =>
            string.Concat(RoleAnimation.Decrypt.ChipFrames(cycle, salt: 404).Where(frame => frame.Tones == "111" && !RoleAnimation.Words.Contains(frame.Text))
                .Select(frame => frame.Text));

        Assert.Equal(Scrambled(0), Scrambled(0));
        Assert.NotEqual(Scrambled(0), Scrambled(1));
        Assert.All(Scrambled(0), glyph => Assert.Contains(glyph, RoleAnimation.Scramble));
    }

    [Fact]
    public void Decrypt_moves_on_a_frame_every_140_ms_and_into_a_fresh_scramble_after_the_last()
    {
        var animation = RoleAnimation.Decrypt;

        Assert.Equal(animation.ChipFrames(0, 7)[0], animation.Chip(TimeSpan.FromMilliseconds(139), 7));
        Assert.Equal(animation.ChipFrames(0, 7)[1], animation.Chip(TimeSpan.FromMilliseconds(140), 7));
        Assert.Equal(animation.ChipFrames(1, 7)[0], animation.Chip(TimeSpan.FromMilliseconds(140 * 72), 7));
        Assert.Equal(animation.HeaderFrames(1, 7)[3], animation.Header(TimeSpan.FromMilliseconds(140 * (56 + 3)), 7));
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void Every_frame_is_one_cell_a_glyph_and_keeps_the_chip_its_width(string role)
    {
        var animation = RoleAnimation.For(role)!;

        Assert.All(animation.ChipFrames(0, 1).Concat(animation.ChipFrames(5, 9)), frame =>
        {
            Assert.Equal(RoleAnimation.ChipCells, frame.Text.Length);
            Assert.Equal(RoleAnimation.ChipCells, frame.Text.GetColumns());
            Assert.Equal(RoleAnimation.ChipCells, frame.Tones.Length);
            Assert.All(frame.Text, glyph => Assert.Equal(1, glyph.ToString().GetColumns()));
        });
        Assert.All(animation.HeaderFrames(0, 1), frame =>
        {
            Assert.Equal(1, frame.Text.GetColumns());
            Assert.Equal(1, frame.Tones.Length);
        });
        Assert.All(RoleAnimation.Scramble, glyph => Assert.Equal(1, glyph.ToString().GetColumns()));
    }

    [Theory]
    [InlineData("dev", "#2f7d48", "#4fd27a", "#e6ffec")]
    [InlineData("lead", "#8d6818", "#f0b23e", "#fff3c8")]
    [InlineData("customer", "#2b6789", "#5cb9ea", "#e2f5ff")]
    public void On_a_dark_theme_each_tone_is_the_role_s_colour_from_the_comment(string role, string dim, string normal, string bright)
    {
        var animation = RoleAnimation.For(role)!;
        var dark = new Attribute(new Color(0xcc, 0xcc, 0xcc), new Color(0x0c, 0x0e, 0x0e));

        Assert.Equal(Color.Parse(dim), OverseerView.ToneAttribute(animation, Tone.Dim, dark).Foreground);
        Assert.Equal(Color.Parse(normal), OverseerView.ToneAttribute(animation, Tone.Normal, dark).Foreground);
        Assert.Equal(Color.Parse(bright), OverseerView.ToneAttribute(animation, Tone.Bright, dark).Foreground);
        Assert.Equal(dark.Background, OverseerView.ToneAttribute(animation, Tone.Bright, dark).Background);
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void On_a_light_theme_the_tones_are_darker_and_still_step_from_faint_to_strong(string role)
    {
        var animation = RoleAnimation.For(role)!;
        var light = new Attribute(new Color(0x20, 0x20, 0x20), new Color(0xfa, 0xfa, 0xfa));
        var tones = Enum.GetValues<Tone>().Select(tone => OverseerView.ToneAttribute(animation, tone, light).Foreground).ToList();

        Assert.True(Lightness(tones[1]) < Lightness(animation.Colour(Tone.Normal, dark: true)));
        Assert.True(Lightness(tones[2]) < Lightness(animation.Colour(Tone.Bright, dark: true)));
        Assert.True(Lightness(tones[0]) > Lightness(tones[1]));
        Assert.True(Lightness(tones[1]) > Lightness(tones[2]));
    }

    private static int Lightness(Color colour) => colour.R + colour.G + colour.B;
}
