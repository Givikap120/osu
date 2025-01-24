// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing
{
    public class OsuDifficultyHitObject : DifficultyHitObject
    {
        private const int normalized_radius = 52;

        protected new OsuHitObject BaseObject => (OsuHitObject)base.BaseObject;

        /// <summary>
        /// Normalized distance from the <see cref="OsuHitObject.StackedPosition"/> of the previous <see cref="OsuDifficultyHitObject"/>.
        /// </summary>
        public double Distance { get; private set; }


        private readonly OsuHitObject lastObject;

        /// <summary>
        /// Initializes the object calculating extra data required for difficulty calculation.
        /// </summary>
        public OsuDifficultyHitObject(HitObject hitObject, HitObject lastObject, HitObject? lastLastObject, double clockRate, List<DifficultyHitObject> objects, int index)
            : base(hitObject, lastObject, clockRate, objects, index)
        {
            this.lastObject = (OsuHitObject)lastObject;

            setDistances();
        }

        private void setDistances()
        {
            // We will scale distances by this factor, so we can assume a uniform CircleSize among beatmaps.
            double scalingFactor = normalized_radius / BaseObject.Radius;
            if (BaseObject.Radius < 30)
            {
                double smallCircleBonus = Math.Min(30 - BaseObject.Radius, 5) / 50;
                scalingFactor *= 1 + smallCircleBonus;
            }

            Distance = (BaseObject.StackedPosition - lastObject.StackedPosition).Length * scalingFactor;
        }
    }
}
