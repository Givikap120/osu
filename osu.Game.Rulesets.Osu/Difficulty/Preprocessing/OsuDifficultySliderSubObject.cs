// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osuTK;

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing
{
    public class OsuDifficultySliderSubObject
    {
        /// <summary>
        /// Raw osu!pixel vector from the center position of the previous slider subobject to the center position of the current slider subobject.
        /// </summary>
        public Vector2 Movement { get; private set; }

        /// <summary>
        /// Milliseconds elapsed since the start time of the previous slider subMovement, with a minimum of 5ms.
        /// </summary>
        public double StrainTime { get; private set; }

        public OsuDifficultySliderSubObject(Vector2 movement, double deltaTime)
        {
            Movement = movement;
            StrainTime = Math.Max(5, deltaTime);
        }
    }
}