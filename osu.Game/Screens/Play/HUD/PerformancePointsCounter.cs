// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Screens.Play.HUD
{
    public abstract partial class PerformancePointsCounter : LiveDifficultyDisplay
    {
        private PerformanceCalculator performanceCalculator;
        private ScoreInfo scoreInfo;
        protected InternalPpCounter PpCounter;

        [BackgroundDependencyLoader]
        private void load()
        {
            if (GameplayState != null)
            {
                performanceCalculator = GameplayState.Ruleset.CreatePerformanceCalculator();
                scoreInfo = new ScoreInfo(GameplayState.Score.ScoreInfo.BeatmapInfo, GameplayState.Score.ScoreInfo.Ruleset) { Mods = ClonedMods };
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            PpCounter.IsValid = IsValid;
        }

        public override bool IsValid
        {
            get => base.IsValid;
            set
            {
                if (value == IsValid)
                    return;

                base.IsValid = value;
                if (PpCounter != null) PpCounter.IsValid = value;
            }
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

            ScoreProcessor.PopulateScore(scoreInfo);
            int newPp = (int)Math.Round(performanceCalculator?.Calculate(scoreInfo, attrib).Total ?? 0, MidpointRounding.AwayFromZero);
            PpCounter.Current.Value = newPp;
        }

        protected abstract partial class InternalPpCounter : RollingCounter<int>
        {
            private const float alpha_when_invalid = 0.3f;
            protected abstract Drawable Text { get; }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                Text.FadeTo(isValid ? 1 : alpha_when_invalid, 1000, Easing.OutQuint);
            }

            private bool isValid;

            public bool IsValid
            {
                get => isValid;
                set
                {
                    if (value == isValid)
                        return;

                    isValid = value;
                    Text?.FadeTo(value ? 1 : alpha_when_invalid, 1000, Easing.OutQuint);
                }
            }
        }
    }
}
