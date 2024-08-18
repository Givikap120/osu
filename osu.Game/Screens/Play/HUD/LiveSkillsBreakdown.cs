// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using JetBrains.Annotations;
using osu.Game.Skinning;
using osu.Framework.Allocation;
using osu.Game.Rulesets.Judgements;
using osu.Game.Beatmaps;
using osu.Framework.Graphics;
using osuTK;


namespace osu.Game.Screens.Play.HUD
{
    [UsedImplicitly]
    public partial class LiveSkillsBreakdown : LiveDifficultyDisplay, ISerialisableDrawable
    {
        private SkillsBreakdownBase skillsBreakdown = null!;

        public LiveSkillsBreakdown()
        {
            AutoSizeAxes = Axes.None;
            Size = new Vector2(50);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Child = skillsBreakdown = new SkillsBreakdownBase()
            {
                RelativeSizeAxes = Axes.Both
            };
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

            skillsBreakdown.UpdateSkillsBreakdown(new StarDifficulty(attrib));
        }
    }
}
