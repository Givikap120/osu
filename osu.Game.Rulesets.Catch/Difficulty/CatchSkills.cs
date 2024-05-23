// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.Catch.Difficulty.Skills;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Catch.Difficulty
{
    public class CatchSkills(Mod[] mods, double clockRate) : SkillsBase
    {
        public readonly Movement Movement = new(mods, clockRate);

        public override void Process(DifficultyHitObject current)
        {
            Movement.Process(current);
        }

        public double DifficultyValue() => Movement.DifficultyValue();
    }
}

