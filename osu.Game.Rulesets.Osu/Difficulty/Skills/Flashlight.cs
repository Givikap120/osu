// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to memorise and hit every object in a map with the Flashlight mod enabled.
    /// </summary>
    public class Flashlight : OsuStrainSkill
    {
        public Flashlight(Mod[] mods) : base(mods)
        {
        }

        protected override double CalculateInitialStrain(double time, DifficultyHitObject current)
        {
            throw new NotImplementedException();
        }

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            throw new NotImplementedException();
        }
    }
}
