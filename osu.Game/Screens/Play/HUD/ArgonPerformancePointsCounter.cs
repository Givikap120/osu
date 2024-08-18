// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.LocalisationExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Game.Configuration;
using osu.Game.Localisation.SkinComponents;
using osu.Game.Resources.Localisation.Web;
using osu.Game.Skinning;

namespace osu.Game.Screens.Play.HUD
{
    public partial class ArgonPerformancePointsCounter : PerformancePointsCounter, ISerialisableDrawable
    {

        [SettingSource("Wireframe opacity", "Controls the opacity of the wireframes behind the digits.")]
        public BindableFloat WireframeOpacity { get; } = new BindableFloat(0.25f)
        {
            Precision = 0.01f,
            MinValue = 0,
            MaxValue = 1,
        };

        [SettingSource(typeof(SkinnableComponentStrings), nameof(SkinnableComponentStrings.ShowLabel), nameof(SkinnableComponentStrings.ShowLabelDescription))]
        public Bindable<bool> ShowLabel { get; } = new BindableBool(true);


        [BackgroundDependencyLoader]
        private void load()
        {
            AddInternal(PpCounter = new ArgonCounter(WireframeOpacity, ShowLabel));
        }

        private partial class ArgonCounter : InternalPpCounter
        {
            private ArgonCounterTextComponent text = null!;

            private readonly Bindable<float> wireframeOpacity;
            private readonly Bindable<bool> showLabel;

            public ArgonCounter(Bindable<float> wireframeOpacity, Bindable<bool> showLabel)
            {
                this.wireframeOpacity = wireframeOpacity;
                this.showLabel = showLabel;
            }

            protected override double RollingDuration => 250;

            public override int DisplayedCount
            {
                get => base.DisplayedCount;
                set
                {
                    base.DisplayedCount = value;
                    updateWireframe();
                }
            }

            protected override Drawable Text => text;

            private void updateWireframe()
            {
                int digitsRequiredForDisplayCount = Math.Max(3, getDigitsRequiredForDisplayCount());

                if (digitsRequiredForDisplayCount != text.WireframeTemplate.Length)
                    text.WireframeTemplate = new string('#', digitsRequiredForDisplayCount);
            }

            private int getDigitsRequiredForDisplayCount()
            {
                int digitsRequired = 1;
                long c = DisplayedCount;
                while ((c /= 10) > 0)
                    digitsRequired++;
                return digitsRequired;
            }

            protected override IHasText CreateText() => text = new ArgonCounterTextComponent(Anchor.TopRight, BeatmapsetsStrings.ShowScoreboardHeaderspp.ToUpper())
            {
                WireframeOpacity = { BindTarget = wireframeOpacity },
                ShowLabel = { BindTarget = showLabel },
            };
        }
    }
}
