// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.Difficulty.Preprocessing;

namespace osu.Game.Rulesets.Difficulty
{
    public abstract class SkillsBase
    {
        public abstract void Process(DifficultyHitObject current);
    }
}
