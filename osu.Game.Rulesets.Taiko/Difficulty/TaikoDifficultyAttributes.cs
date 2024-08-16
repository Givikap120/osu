﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;

namespace osu.Game.Rulesets.Taiko.Difficulty
{
    public class TaikoDifficultyAttributes : DifficultyAttributes
    {
        /// <summary>
        /// The difficulty corresponding to the stamina skill.
        /// </summary>
        [JsonProperty("stamina_difficulty")]
        public double StaminaDifficulty { get; set; }

        /// <summary>
        /// The difficulty corresponding to the rhythm skill.
        /// </summary>
        [JsonProperty("rhythm_difficulty")]
        public double RhythmDifficulty { get; set; }

        /// <summary>
        /// The difficulty corresponding to the colour skill.
        /// </summary>
        [JsonProperty("colour_difficulty")]
        public double ColourDifficulty { get; set; }

        /// <summary>
        /// The difficulty corresponding to the hardest parts of the map.
        /// </summary>
        [JsonProperty("peak_difficulty")]
        public double PeakDifficulty { get; set; }

        /// <summary>
        /// The perceived hit window for a GREAT hit inclusive of rate-adjusting mods (DT/HT/etc).
        /// </summary>
        /// <remarks>
        /// Rate-adjusting mods don't directly affect the hit window, but have a perceived effect as a result of adjusting audio timing.
        /// </remarks>
        [JsonProperty("great_hit_window")]
        public double GreatHitWindow { get; set; }

        public override IEnumerable<(int attributeId, object value)> ToDatabaseAttributes()
        {
            foreach (var v in base.ToDatabaseAttributes())
                yield return v;

            yield return (ATTRIB_ID_DIFFICULTY, StarRating);
            yield return (ATTRIB_ID_GREAT_HIT_WINDOW, GreatHitWindow);
        }

        public override void FromDatabaseAttributes(IReadOnlyDictionary<int, double> values, IBeatmapOnlineInfo onlineInfo)
        {
            base.FromDatabaseAttributes(values, onlineInfo);

            StarRating = values[ATTRIB_ID_DIFFICULTY];
            GreatHitWindow = values[ATTRIB_ID_GREAT_HIT_WINDOW];
        }

        public override SkillValue[] GetSkillValues()
        {
            //double aimPerformanceWithoutSliders = OsuStrainSkill.DifficultyToPerformance(AimDifficulty * SliderFactor);
            //double speedPerformance = OsuStrainSkill.DifficultyToPerformance(SpeedDifficulty);
            //double flashlightPerformance = Flashlight.DifficultyToPerformance(FlashlightDifficulty);



            return [
                new SkillValue { Value = difficultyRescale(ColourDifficulty), SkillName = "Colour" },
                new SkillValue { Value = difficultyRescale(RhythmDifficulty), SkillName = "Rhythm" },
                new SkillValue { Value = difficultyRescale(StaminaDifficulty), SkillName = "Stamina" },
            ];
        }

        private static double difficultyRescale(double difficulty) => 10.43 * Math.Log(difficulty * 1.4 / 8 + 1);
    }
}
