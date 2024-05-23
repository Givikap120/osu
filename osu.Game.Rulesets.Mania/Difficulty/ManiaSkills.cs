// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Mania.Difficulty.Skills;
using osu.Game.Rulesets.Mania.Beatmaps;

namespace osu.Game.Rulesets.Mania.Difficulty
{
    public class ManiaSkills(IBeatmap beatmap, Mod[] mods) : SkillsBase
    {
        public readonly Strain Strain = new Strain(mods, ((ManiaBeatmap)beatmap).TotalColumns);

        public override void Process(DifficultyHitObject current)
        {
            Strain.Process(current);
        }

        public double DifficultyValue() => Strain.DifficultyValue();
    }
}

