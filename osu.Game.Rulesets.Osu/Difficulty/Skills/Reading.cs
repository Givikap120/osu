// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    public class Reading : HarmonicSkill
    {
        public Reading(Mod[] mods) : base(mods)
        {
        }

        protected override double ObjectDifficultyOf(DifficultyHitObject current)
        {
            throw new NotImplementedException();
        }
    }
}
