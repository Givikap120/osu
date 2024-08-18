// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Game.Rulesets.Judgements;
using osu.Game.Skinning;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Beatmaps;
using osu.Framework.Graphics;

namespace osu.Game.Screens.Play.HUD
{
    public partial class StarRatingCounter : LiveDifficultyDisplay, ISerialisableDrawable
    {
        private StarRatingDisplay starRatingDisplay = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            AddInternal(starRatingDisplay = new StarRatingDisplay(default, animated: true));
        }

        protected override void OnJudgementChanged(JudgementResult judgement)
        {
            base.OnJudgementChanged(judgement);

            var attrib = GetAttributeAtTime(judgement);
            if (attrib == null)
            {
                IsValid = false;
                return;
            }

            starRatingDisplay.Current.Value = new StarDifficulty(attrib);
        }
    }
}
