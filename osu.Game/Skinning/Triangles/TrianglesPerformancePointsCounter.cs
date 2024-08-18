// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Resources.Localisation.Web;
using osu.Game.Screens.Play.HUD;
using osuTK;

namespace osu.Game.Skinning.Triangles
{
    public partial class TrianglesPerformancePointsCounter : PerformancePointsCounter, ISerialisableDrawable
    {
        [BackgroundDependencyLoader]
        private void load()
        {
            AddInternal(PpCounter = new TrianglesCounter());
        }

        private partial class TrianglesCounter : InternalPpCounter
        {
            protected override bool IsRollingProportional => true;

            protected override double RollingDuration => 500;


            [BackgroundDependencyLoader]
            private void load(OsuColour colours)
            {
                Colour = colours.BlueLighter;
            }
            protected override Drawable Text => DrawableCount;

            protected override LocalisableString FormatCount(int count) => count.ToString(@"D");

            protected override IHasText CreateText() => new TextComponent();

            private partial class TextComponent : CompositeDrawable, IHasText
            {
                public LocalisableString Text
                {
                    get => text.Text;
                    set => text.Text = value;
                }

                private readonly OsuSpriteText text;

                public TextComponent()
                {
                    AutoSizeAxes = Axes.Both;

                    InternalChild = new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Spacing = new Vector2(2),
                        Children = new Drawable[]
                        {
                        text = new OsuSpriteText
                        {
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomLeft,
                            Font = OsuFont.Numeric.With(size: 16, fixedWidth: true)
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomLeft,
                            Text = BeatmapsetsStrings.ShowScoreboardHeaderspp,
                            Font = OsuFont.Numeric.With(size: 8),
                            Padding = new MarginPadding { Bottom = 1.5f }, // align baseline better
                        }
                        }
                    };
                }
            }
        }
    }
}
