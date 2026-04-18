// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    public class Flashlight : StrainSkill
    {
        public Flashlight(Mod[] mods) : base(mods)
        {
        }

        protected override double CalculateInitialStrain(double time, DifficultyHitObject current)
        {
            throw new System.NotImplementedException();
        }

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            throw new System.NotImplementedException();
        }
    }
}
