// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Skills;
using osu.Game.Rulesets.Osu.Mods;

namespace osu.Game.Rulesets.Osu.Difficulty
{
    public class OsuSkills(Mod[] mods) : SkillsBase
    {
        public readonly Aim Aim = new(mods, true);
        public readonly Aim AimNoSliders = new(mods, false);
        public readonly Speed Speed = new(mods);
        public readonly Flashlight? Flashlight = mods.Any(h => h is OsuModFlashlight) ? new(mods) : null;

        public override void Process(DifficultyHitObject current)
        {
            Aim.Process(current);
            AimNoSliders.Process(current);
            Speed.Process(current);
            Flashlight?.Process(current);
        }
    }
}
